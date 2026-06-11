using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Support;
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Factory for creating and managing NovelAgentWorkspace instances.
/// Implements project-level singleton pattern with LRU caching and reference counting.
/// Thread-safe using ConcurrentDictionary and SemaphoreSlim.
/// </summary>
public sealed class WorkspaceFactory : IWorkspaceFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, WorkspaceEntry> _cache = new();
    private readonly WorkspaceFactoryOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Timer _evictionTimer;

    // Statistics
    private long _cacheHits = 0;
    private long _cacheMisses = 0;
    private long _evictionCount = 0;
    private long _totalDatabaseLoadTimeMs = 0;
    private long _databaseLoadCount = 0;

    public WorkspaceFactory(
        IOptions<WorkspaceFactoryOptions> options,
        IServiceProvider serviceProvider)
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;

        // Start background eviction timer (runs every 30 seconds)
        _evictionTimer = new Timer(
            _ => EvictIdleWorkspacesAsync().Wait(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    public async Task<WorkspaceEntry> AcquireAsync(string userId, string projectId, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(userId, projectId);

        // Fast path: cache hit
        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            Interlocked.Increment(ref _cacheHits);
            entry.AcquireLease();
            return entry;
        }

        // Slow path: create new Workspace
        Interlocked.Increment(ref _cacheMisses);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(cacheKey, out entry))
            {
                entry.AcquireLease();
                return entry;
            }

            // Enforce cache size limit before creating new instance
            if (_cache.Count >= _options.MaxCachedWorkspaces)
            {
                await EvictOldestIdleWorkspaceAsync();
            }

            // Create new Workspace instance
            var workspace = await CreateWorkspaceAsync(userId, projectId, cancellationToken);

            entry = new WorkspaceEntry
            {
                UserId = userId,
                ProjectId = projectId,
                Workspace = workspace
            };

            entry.AcquireLease();
            _cache[cacheKey] = entry;

            return entry;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Release(string userId, string projectId)
    {
        var cacheKey = GetCacheKey(userId, projectId);

        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            entry.ReleaseLease();
        }
    }

    public void Touch(string userId, string projectId)
    {
        var cacheKey = GetCacheKey(userId, projectId);

        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            entry.Touch();
        }
    }

    public WorkspaceFactoryStats GetStats()
    {
        var entries = _cache.Values.ToList();

        return new WorkspaceFactoryStats
        {
            TotalWorkspaces = entries.Count,
            ActiveReferences = entries.Sum(e => e.ActiveReferences),
            IdleWorkspaces = entries.Count(e => e.ActiveReferences == 0),
            TotalEvictions = _evictionCount,
            CacheHits = _cacheHits,
            CacheMisses = _cacheMisses
        };
    }

    public bool IsWorkspaceActive(string userId, string projectId)
    {
        var cacheKey = GetCacheKey(userId, projectId);
        return _cache.ContainsKey(cacheKey);
    }

    public WorkspaceFactoryStats GetDetailedStats()
    {
        var entries = _cache.Values.ToList();
        var now = DateTime.UtcNow;

        TimeSpan? oldestAge = null;
        if (entries.Any())
        {
            var oldestEntry = entries.OrderBy(e => e.LastAccessTime).First();
            oldestAge = now - oldestEntry.LastAccessTime;
        }

        TimeSpan? avgLoadTime = null;
        var loadCount = Interlocked.Read(ref _databaseLoadCount);
        if (loadCount > 0)
        {
            var totalMs = Interlocked.Read(ref _totalDatabaseLoadTimeMs);
            avgLoadTime = TimeSpan.FromMilliseconds((double)totalMs / loadCount);
        }

        return new WorkspaceFactoryStats
        {
            TotalWorkspaces = entries.Count,
            ActiveReferences = entries.Sum(e => e.ActiveReferences),
            IdleWorkspaces = entries.Count(e => e.ActiveReferences == 0),
            TotalEvictions = _evictionCount,
            CacheHits = _cacheHits,
            CacheMisses = _cacheMisses,
            ActiveWorkspacesCount = entries.Count,
            OldestWorkspaceAge = oldestAge,
            AverageDatabaseLoadTime = avgLoadTime
        };
    }

    public async Task<WorkspaceFactoryHealthStatus> CheckHealthAsync()
    {
        var stats = GetDetailedStats();
        var isHealthy = true;
        var messages = new List<string>();

        // Check cache capacity
        if (stats.TotalWorkspaces >= _options.MaxCachedWorkspaces)
        {
            isHealthy = false;
            messages.Add($"Cache at capacity: {stats.TotalWorkspaces}/{_options.MaxCachedWorkspaces}");
        }

        // Check eviction rate (> 100/min indicates pressure)
        var evictionRate = stats.TotalEvictions;
        if (evictionRate > 100)
        {
            messages.Add($"High eviction count: {evictionRate}");
        }

        // Test database connectivity
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
        }
        catch (Exception ex)
        {
            isHealthy = false;
            messages.Add($"Database connectivity failed: {ex.Message}");
        }

        return new WorkspaceFactoryHealthStatus
        {
            IsHealthy = isHealthy,
            Message = messages.Any() ? string.Join("; ", messages) : "All checks passed",
            LastCheckTimestamp = DateTime.UtcNow
        };
    }

    public WorkspaceStatus GetWorkspaceStatus(string userId, string projectId)
    {
        var cacheKey = GetCacheKey(userId, projectId);

        if (!_cache.TryGetValue(cacheKey, out var entry))
        {
            return WorkspaceStatus.NotFound;
        }

        // Check if being evicted (this is a simplification - true eviction tracking would need state field)
        var idleTimeout = TimeSpan.FromMinutes(_options.IdleTimeoutMinutes);
        if (entry.IsEvictable(idleTimeout))
        {
            return WorkspaceStatus.Evicting;
        }

        // Check reference count
        if (entry.ActiveReferences > 0)
        {
            return WorkspaceStatus.Active;
        }

        return WorkspaceStatus.Idle;
    }

    public void ForceRelease(string userId, string projectId)
    {
        var cacheKey = GetCacheKey(userId, projectId);

        if (_cache.TryRemove(cacheKey, out var entry))
        {
            // Force set reference count to 0 (access private field via reflection is not recommended)
            // Just remove from cache - the entry object will be garbage collected
            Interlocked.Increment(ref _evictionCount);
        }
    }

    public void ClearAll()
    {
        var count = _cache.Count;
        _cache.Clear();
        Interlocked.Add(ref _evictionCount, count);
    }

    private static string GetCacheKey(string userId, string projectId)
    {
        return $"{userId}:{projectId}";
    }

    private async Task<NovelAgentWorkspace> CreateWorkspaceAsync(
        string userId, string projectId, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();

        // Load project to verify it exists and belongs to user when it is already in EF.
        // Legacy JSON projects are hydrated after the workspace is available.
        var project = await db.NovelProjects
            .Where(p => p.Id == projectId && p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        // Security check: if project exists but belongs to another user, deny access
        if (project == null && await db.NovelProjects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            throw new InvalidOperationException($"Project {projectId} not found for user {userId}");
        }

        // Get required services
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var settingsManager = scope.ServiceProvider.GetRequiredService<UserSettingsManager>();

        // Create Workspace instance
        var workspace = new NovelAgentWorkspace(env, config, settingsManager) { UserId = userId };

        // If project doesn't exist and it's not a temp ID, try legacy hydration
        // If still not found, treat as temp project instead of throwing
        if (project == null && !IsTemporaryProjectId(projectId))
        {
            project = await TryHydrateLegacyProjectAsync(
                db,
                workspace,
                userId,
                projectId,
                cancellationToken);

            // Don't throw if project not found - workspace can work without project for casual chat
        }

        // Track load time
        var loadTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        Interlocked.Add(ref _totalDatabaseLoadTimeMs, loadTimeMs);
        Interlocked.Increment(ref _databaseLoadCount);

        return workspace;
    }

    private static bool IsTemporaryProjectId(string projectId) =>
        string.IsNullOrWhiteSpace(projectId) ||
        projectId.StartsWith("temp-", StringComparison.OrdinalIgnoreCase);

    private static async Task<NovelProject?> TryHydrateLegacyProjectAsync(
        NovelAgentDbContext db,
        NovelAgentWorkspace workspace,
        string userId,
        string projectId,
        CancellationToken cancellationToken)
    {
        var catalog = new NovelProjectCatalog(workspace);
        var legacyProject = await catalog.FindAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (legacyProject == null)
        {
            return null;
        }

        var project = new NovelProject
        {
            Id = legacyProject.Id,
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(legacyProject.Title) ? "未命名小说" : legacyProject.Title,
            Genre = EmptyToNull(legacyProject.Genre),
            SubGenre = EmptyToNull(legacyProject.SubGenre),
            CoreHook = EmptyToNull(legacyProject.CoreHook),
            Status = string.IsNullOrWhiteSpace(legacyProject.Status) ? "draft" : legacyProject.Status,
            StorageProjectName = EmptyToNull(legacyProject.StorageProjectName),
            WordCount = 0,
            CreatedAt = legacyProject.CreatedAt == default ? DateTime.UtcNow : legacyProject.CreatedAt,
            UpdatedAt = legacyProject.UpdatedAt == default ? DateTime.UtcNow : legacyProject.UpdatedAt
        };

        db.NovelProjects.Add(project);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return project;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task EvictOldestIdleWorkspaceAsync()
    {
        var idleTimeout = TimeSpan.FromMinutes(_options.IdleTimeoutMinutes);

        var evictable = _cache.Values
            .Where(e => e.IsEvictable(idleTimeout))
            .OrderBy(e => e.LastAccessTime)
            .FirstOrDefault();

        if (evictable != null)
        {
            var cacheKey = GetCacheKey(evictable.UserId, evictable.ProjectId);
            if (_cache.TryRemove(cacheKey, out _))
            {
                Interlocked.Increment(ref _evictionCount);
            }
        }
    }

    private Task EvictIdleWorkspacesAsync()
    {
        var idleTimeout = TimeSpan.FromMinutes(_options.IdleTimeoutMinutes);

        var evictableEntries = _cache.Values
            .Where(e => e.IsEvictable(idleTimeout))
            .ToList();

        foreach (var entry in evictableEntries)
        {
            var cacheKey = GetCacheKey(entry.UserId, entry.ProjectId);
            if (_cache.TryRemove(cacheKey, out _))
            {
                Interlocked.Increment(ref _evictionCount);
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _evictionTimer?.Dispose();
        _lock?.Dispose();
    }
}

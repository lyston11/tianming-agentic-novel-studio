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
using TM.Web.NovelAgentWeb.Support;

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

    private static string GetCacheKey(string userId, string projectId)
    {
        return $"{userId}:{projectId}";
    }

    private async Task<NovelAgentWorkspace> CreateWorkspaceAsync(
        string userId, string projectId, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();

        // Load project to verify it exists and belongs to user
        var project = await db.NovelProjects
            .Where(p => p.Id == projectId && p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            throw new InvalidOperationException($"Project {projectId} not found for user {userId}");
        }

        // Get required services
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var settingsManager = scope.ServiceProvider.GetRequiredService<UserSettingsManager>();

        // Create Workspace instance
        var workspace = new NovelAgentWorkspace(env, config, settingsManager);

        return workspace;
    }

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

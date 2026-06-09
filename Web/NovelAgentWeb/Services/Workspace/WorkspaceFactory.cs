using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

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

    public Task<WorkspaceEntry> AcquireAsync(string userId, string projectId, CancellationToken cancellationToken = default)
    {
        // Implementation in Task 9
        throw new NotImplementedException();
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

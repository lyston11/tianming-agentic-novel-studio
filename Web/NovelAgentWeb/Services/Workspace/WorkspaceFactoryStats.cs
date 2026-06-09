namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Statistics about WorkspaceFactory cache state.
/// </summary>
public sealed class WorkspaceFactoryStats
{
    /// <summary>
    /// Total number of workspaces currently in cache.
    /// </summary>
    public int TotalWorkspaces { get; init; }

    /// <summary>
    /// Sum of all active references across all workspaces.
    /// </summary>
    public int ActiveReferences { get; init; }

    /// <summary>
    /// Number of workspaces with zero references (eligible for eviction if idle).
    /// </summary>
    public int IdleWorkspaces { get; init; }

    /// <summary>
    /// Total number of times workspaces have been evicted from cache.
    /// </summary>
    public long TotalEvictions { get; init; }

    /// <summary>
    /// Total number of cache hits (workspace already in cache on Acquire).
    /// </summary>
    public long CacheHits { get; init; }

    /// <summary>
    /// Total number of cache misses (new workspace created on Acquire).
    /// </summary>
    public long CacheMisses { get; init; }

    /// <summary>
    /// Cache hit rate (0.0 to 1.0).
    /// </summary>
    public double CacheHitRate => (CacheHits + CacheMisses) == 0 ? 0.0 : (double)CacheHits / (CacheHits + CacheMisses);
}

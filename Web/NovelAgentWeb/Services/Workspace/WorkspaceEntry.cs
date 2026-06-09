using System;
using System.Threading;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Cache entry for a NovelAgentWorkspace with reference counting and LRU tracking.
/// Thread-safe via Interlocked operations.
/// </summary>
public sealed class WorkspaceEntry
{
    public string UserId { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public NovelAgentWorkspace Workspace { get; init; } = null!;

    private int _activeReferences = 0;
    public int ActiveReferences => _activeReferences;

    public DateTime LastAccessTime { get; private set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    private long _totalAccesses = 0;
    public long TotalAccesses => _totalAccesses;

    /// <summary>
    /// Acquire a lease on this workspace. Increments reference count and updates access time.
    /// </summary>
    public void AcquireLease()
    {
        Interlocked.Increment(ref _activeReferences);
        LastAccessTime = DateTime.UtcNow;
        Interlocked.Increment(ref _totalAccesses);
    }

    /// <summary>
    /// Release a lease on this workspace. Decrements reference count and updates access time.
    /// </summary>
    public void ReleaseLease()
    {
        Interlocked.Decrement(ref _activeReferences);
        LastAccessTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Update last access time without changing reference count.
    /// Called when an existing session uses the workspace.
    /// </summary>
    public void Touch()
    {
        LastAccessTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Check if this entry can be evicted.
    /// Entry is evictable if: no active references AND idle longer than timeout.
    /// </summary>
    public bool IsEvictable(TimeSpan idleTimeout)
    {
        return _activeReferences == 0 && DateTime.UtcNow - LastAccessTime > idleTimeout;
    }
}

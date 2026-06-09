using System.Threading;
using System.Threading.Tasks;
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Factory for creating and managing NovelAgentWorkspace instances.
/// Implements project-level singleton pattern with LRU caching and reference counting.
/// </summary>
public interface IWorkspaceFactory
{
    /// <summary>
    /// Acquire a workspace for the given user and project.
    /// If workspace exists in cache, increments reference count.
    /// Otherwise, creates new workspace and adds to cache.
    /// </summary>
    Task<WorkspaceEntry> AcquireAsync(string userId, string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Release a workspace lease, decrementing its reference count.
    /// Workspace remains in cache but becomes eligible for eviction after idle timeout.
    /// </summary>
    void Release(string userId, string projectId);

    /// <summary>
    /// Update last access time for a workspace without changing reference count.
    /// Call this on each API request to prevent premature eviction.
    /// </summary>
    void Touch(string userId, string projectId);

    /// <summary>
    /// Get current factory statistics for monitoring.
    /// </summary>
    WorkspaceFactoryStats GetStats();

    /// <summary>
    /// Check if a workspace is currently active in cache.
    /// </summary>
    bool IsWorkspaceActive(string userId, string projectId);

    /// <summary>
    /// Get detailed statistics including active workspace count and timing metrics.
    /// </summary>
    WorkspaceFactoryStats GetDetailedStats();

    /// <summary>
    /// Perform health diagnostics on the workspace factory.
    /// </summary>
    Task<WorkspaceFactoryHealthStatus> CheckHealthAsync();
}

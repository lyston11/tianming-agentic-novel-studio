using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Service for managing workspace overview and project listings.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Gets workspace overview with all user projects and statistics.
    /// </summary>
    Task<WorkspaceResponse> GetWorkspaceAsync(string userId, CancellationToken ct = default);
}

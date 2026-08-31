namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Configuration options for WorkspaceFactory.
/// </summary>
public sealed class WorkspaceFactoryOptions
{
    /// <summary>
    /// Root directory for workspace storage.
    /// Default: "App_Data"
    /// </summary>
    public string StorageRoot { get; set; } = "App_Data";

    /// <summary>
    /// Maximum number of workspaces to cache.
    /// When limit is reached, least recently used workspace is evicted.
    /// Default: 50
    /// </summary>
    public int MaxCachedWorkspaces { get; set; } = 50;

    /// <summary>
    /// Idle timeout in minutes before a workspace can be evicted.
    /// Workspace must have zero active references AND be idle for this duration.
    /// Default: 30 minutes
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 30;
}

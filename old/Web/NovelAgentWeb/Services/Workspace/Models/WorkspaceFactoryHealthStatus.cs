namespace TM.Web.NovelAgentWeb.Services.Workspace.Models;

public sealed class WorkspaceFactoryHealthStatus
{
    public bool IsHealthy { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime LastCheckTimestamp { get; set; } = DateTime.UtcNow;
}

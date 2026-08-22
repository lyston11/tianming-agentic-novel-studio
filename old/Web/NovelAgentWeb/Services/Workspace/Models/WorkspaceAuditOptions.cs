namespace TM.Web.NovelAgentWeb.Services.Workspace.Models;

public class WorkspaceAuditOptions
{
    public bool EnableStrictMode { get; set; } = false;
    public bool LogViolations { get; set; } = true;
    public bool TrackViolations { get; set; } = true;
    public int AlertThreshold { get; set; } = 5;  // Alert after 5 violations
    public bool EnableAlerts { get; set; } = false;  // Disabled by default
}

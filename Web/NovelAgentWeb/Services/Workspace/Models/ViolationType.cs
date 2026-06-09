namespace TM.Web.NovelAgentWeb.Services.Workspace.Models;

public enum ViolationType
{
    WorkspaceNotAcquired,      // Workspace not acquired before use
    WorkspaceExpired,          // Lease expired
    WorkspaceMismatch,         // Cross-project contamination
    ConcurrentAccessDetected   // Concurrent access anomaly
}

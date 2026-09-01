// ViolationType.cs
namespace TM.Web.NovelAgentWeb.Services.Workspace.Models;

public enum ViolationType
{
    MissingUserId,
    MissingProjectId,
    WorkspaceNotActive,
    WorkspaceNotAcquired,
    WorkspaceExpired,
    WorkspaceMismatch,
    ConcurrentAccessDetected
}

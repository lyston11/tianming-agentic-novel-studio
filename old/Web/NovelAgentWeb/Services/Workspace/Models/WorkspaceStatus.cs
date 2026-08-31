namespace TM.Web.NovelAgentWeb.Services.Workspace.Models;

public enum WorkspaceStatus
{
    NotFound,      // Not in cache
    Active,        // Currently in use (reference count > 0)
    Idle,          // In cache but not in use (reference count = 0)
    Evicting       // Being evicted from cache
}

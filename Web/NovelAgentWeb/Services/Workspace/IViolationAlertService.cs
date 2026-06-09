using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public interface IViolationAlertService
{
    Task SendAlertAsync(WorkspaceViolation violation);
}

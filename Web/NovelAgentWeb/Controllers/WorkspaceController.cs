using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/workspace")]
[Authorize]
public sealed class WorkspaceController : ControllerBase
{
    private readonly IWorkspaceFactory _workspaceFactory;
    private readonly ICurrentUserService _currentUserService;

    public WorkspaceController(
        IWorkspaceFactory workspaceFactory,
        ICurrentUserService currentUserService)
    {
        _workspaceFactory = workspaceFactory;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var tempProjectId = $"temp-{userId}";
        var entry = await _workspaceFactory.AcquireAsync(userId, tempProjectId, ct);
        try
        {
            var catalog = new NovelProjectCatalog(entry.Workspace);
            var document = await catalog.GetAsync(ct);
            var activeProject = document.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, document.ActiveProjectId, StringComparison.OrdinalIgnoreCase))
                ?? document.Projects.FirstOrDefault();

            return Ok(new
            {
                projectName = activeProject?.Title ?? entry.Workspace.ProjectName,
                activeProjectId = activeProject?.Id ?? string.Empty,
                storageProjectName = activeProject?.StorageProjectName ?? entry.Workspace.ProjectName,
                projectCount = document.Projects.Count
            });
        }
        finally
        {
            entry.ReleaseLease();
        }
    }
}

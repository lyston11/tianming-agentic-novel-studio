using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Workspace;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/workspace")]
[Authorize]
public sealed class WorkspaceController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<WorkspaceController> _logger;

    public WorkspaceController(
        IWorkspaceService workspaceService,
        ICurrentUserService currentUserService,
        ILogger<WorkspaceController> logger)
    {
        _workspaceService = workspaceService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var workspace = await _workspaceService.GetWorkspaceAsync(userId, ct);
            return Ok(workspace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workspace");
            return StatusCode(500, ApiErrors.Internal("Failed to get workspace"));
        }
    }
}

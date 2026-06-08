using Microsoft.AspNetCore.Mvc;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class VolumeArcController : ControllerBase
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly ProjectScopedExecutor _projectScope;

    public VolumeArcController(NovelAgentWorkspace workspace, ProjectScopedExecutor projectScope)
    {
        _workspace = workspace;
        _projectScope = projectScope;
    }

    [HttpPost("volume-arc")]
    public async Task<IActionResult> Plan([FromBody] VolumeArcPlanningRequest request, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.PlanVolumeArcAsync(request, ct), ct));

    [HttpPost("volume-arc/{runId}/commit")]
    public async Task<IActionResult> Commit(
        string runId,
        [FromBody] ConfirmRequest request,
        CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.CommitVolumeArcAsync(
            runId,
            request.Overwrite,
            request.Confirmed,
            ct), ct));
}

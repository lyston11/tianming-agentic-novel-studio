using Microsoft.AspNetCore.Mvc;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class StoryFoundationController : ControllerBase
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly ProjectScopedExecutor _projectScope;

    public StoryFoundationController(NovelAgentWorkspace workspace, ProjectScopedExecutor projectScope)
    {
        _workspace = workspace;
        _projectScope = projectScope;
    }

    [HttpPost("story-foundation")]
    public async Task<IActionResult> Plan([FromBody] StoryFoundationRequest request, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.PlanStoryFoundationAsync(request, ct), ct));

    [HttpPost("story-foundation/{runId}/commit")]
    public async Task<IActionResult> Commit(
        string runId,
        [FromBody] CommitStoryFoundationRequest request,
        CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.CommitStoryFoundationAsync(
            runId,
            request.Overwrite,
            request.Confirmed,
            request.SelectedMacroCandidateTitle,
            request.SelectedMacroCandidateId,
            request.SelectedMacroCandidateIndex,
            ct), ct));
}

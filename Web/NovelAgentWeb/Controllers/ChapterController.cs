using Microsoft.AspNetCore.Mvc;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class ChapterController : ControllerBase
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly ProjectScopedExecutor _projectScope;

    public ChapterController(NovelAgentWorkspace workspace, ProjectScopedExecutor projectScope)
    {
        _workspace = workspace;
        _projectScope = projectScope;
    }

    [HttpPost("chapter-plan")]
    public async Task<IActionResult> Plan([FromBody] ChapterCreativeRequest request, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.PlanChapterAsync(request, ct), ct));

    [HttpPost("chapter-candidate/{runId}/select")]
    public async Task<IActionResult> Select(
        string runId,
        [FromBody] SelectChapterCandidateRequest request,
        CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.SelectChapterCandidateAsync(
            runId,
            request.CandidateTitles,
            request.SelectionMode,
            request.SelectionRationale,
            request.Confirmed,
            ct), ct));

    [HttpPost("chapter/{runId}/execute")]
    public async Task<IActionResult> Execute(
        string runId,
        [FromBody] ConfirmOnlyRequest request,
        CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.ExecuteChapterFromBriefAsync(
            runId,
            request.Confirmed,
            ct), ct));
}

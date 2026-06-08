using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class RunController : ControllerBase
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly ProjectScopedExecutor _projectScope;

    public RunController(NovelAgentWorkspace workspace, ProjectScopedExecutor projectScope)
    {
        _workspace = workspace;
        _projectScope = projectScope;
    }

    [HttpGet("runs")]
    public async Task<IActionResult> List(int? take, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.ListRunsAsync(take ?? 30, ct), ct));

    [HttpGet("run/{runId}")]
    public async Task<IActionResult> Get(string runId, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.ResumeRunAsync(runId, ct), ct));

    [HttpPost("run/{runId}/continue")]
    public async Task<IActionResult> Continue(
        string runId,
        [FromBody] ContinueRunRequest request,
        CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.ContinueAgentRunAsync(
            runId,
            request.MaxAutoRisk,
            ct,
            request.MaxAutoSteps), ct));

    [HttpPost("run/{runId}/review")]
    public async Task<IActionResult> Review(string runId, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.ReviewGeneratedChapterAsync(runId, ct), ct));

    [HttpPost("run/{runId}/rewrite")]
    public async Task<IActionResult> Rewrite(
        string runId,
        [FromBody] ConfirmOnlyRequest request,
        CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.RewriteChapterFromReviewAsync(
            runId,
            request.Confirmed,
            ct), ct));
}

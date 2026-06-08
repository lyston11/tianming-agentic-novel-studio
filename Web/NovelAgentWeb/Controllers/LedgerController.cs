using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class LedgerController : ControllerBase
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly ProjectScopedExecutor _projectScope;

    public LedgerController(NovelAgentWorkspace workspace, ProjectScopedExecutor projectScope)
    {
        _workspace = workspace;
        _projectScope = projectScope;
    }

    [HttpPost("run/{runId}/canon/import")]
    public async Task<IActionResult> ImportCanon(string runId, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.ImportProposedCanonFromReviewAsync(runId, ct), ct));

    [HttpPost("run/{runId}/canon/promote")]
    public async Task<IActionResult> PromoteCanon(
        string runId,
        [FromBody] EntryConfirmRequest request,
        CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.PromoteProposedCanonFromRunAsync(
            runId,
            request.EntryIds,
            request.Confirmed,
            ct), ct));

    [HttpPost("run/{runId}/foreshadow/import")]
    public async Task<IActionResult> ImportForeshadow(string runId, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.ImportForeshadowFromReviewAsync(runId, ct), ct));

    [HttpPost("run/{runId}/foreshadow/confirm")]
    public async Task<IActionResult> ConfirmForeshadow(
        string runId,
        [FromBody] EntryConfirmRequest request,
        CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.ConfirmForeshadowStatusFromRunAsync(
            runId,
            request.EntryIds,
            request.Confirmed,
            ct), ct));

    [HttpPost("run/{runId}/character/import")]
    public async Task<IActionResult> ImportCharacter(string runId, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.ImportCharacterStateFromReviewAsync(runId, ct), ct));

    [HttpPost("run/{runId}/character/confirm")]
    public async Task<IActionResult> ConfirmCharacter(
        string runId,
        [FromBody] EntryConfirmRequest request,
        CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.ConfirmCharacterStateFromRunAsync(
            runId,
            request.EntryIds,
            request.Confirmed,
            ct), ct));
}

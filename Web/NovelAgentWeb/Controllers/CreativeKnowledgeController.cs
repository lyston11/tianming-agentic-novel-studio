using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class CreativeKnowledgeController : ControllerBase
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly ProjectScopedExecutor _projectScope;

    public CreativeKnowledgeController(NovelAgentWorkspace workspace, ProjectScopedExecutor projectScope)
    {
        _workspace = workspace;
        _projectScope = projectScope;
    }

    [HttpPost("creative-knowledge/search")]
    public async Task<IActionResult> Search([FromBody] CreativeKnowledgeQueryRequest request, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.RetrieveCreativeKnowledgeAsync(request.Query, ct), ct));

    [HttpPost("creative-knowledge/used-pattern")]
    public async Task<IActionResult> RecordUsedPattern([FromBody] UsedPatternRequest request, CancellationToken ct) =>
        Ok(await _projectScope.RunActiveAsync(() => _workspace.Orchestrator.RecordUsedPlotPatternAsync(
            request.ChapterId,
            request.Pattern,
            request.Note,
            ct), ct));
}

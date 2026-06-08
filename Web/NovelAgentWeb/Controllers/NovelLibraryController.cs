using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class NovelLibraryController : ControllerBase
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly NovelProjectCatalog _catalog;
    private readonly AgentSessionManager _sessionManager;

    public NovelLibraryController(
        NovelAgentWorkspace workspace,
        NovelProjectCatalog catalog,
        AgentSessionManager sessionManager)
    {
        _workspace = workspace;
        _catalog = catalog;
        _sessionManager = sessionManager;
    }

    [HttpGet("novel-library")]
    public async Task<IActionResult> Get([FromQuery] string? projectId, CancellationToken ct)
    {
        var document = await NovelLibrary.BuildAsync(_workspace, _catalog, projectId, ct);
        return document == null ? NotFound("Novel project not found.") : Ok(document);
    }

    [HttpPost("novel-projects")]
    public async Task<IActionResult> Create([FromBody] NovelProjectCreateRequest request, CancellationToken ct) =>
        Ok(await _catalog.CreateAsync(request, ct));

    [HttpPost("novel-projects/{projectId}/activate")]
    public async Task<IActionResult> Activate(string projectId, CancellationToken ct) =>
        Ok(await _catalog.ActivateAsync(projectId, ct));

    [HttpGet("novel-projects/{projectId}/workflow")]
    public async Task<IActionResult> Workflow(string projectId, CancellationToken ct)
    {
        var document = await ProjectWorkflow.BuildAsync(_workspace, _catalog, _sessionManager, projectId, ct);
        return document == null ? NotFound("Novel project not found.") : Ok(document);
    }

    [HttpPatch("novel-projects/{projectId}")]
    public async Task<IActionResult> Update(string projectId, [FromBody] NovelProjectUpdateRequest request, CancellationToken ct)
    {
        var project = await _catalog.UpdateAsync(projectId, request, ct);
        return project == null ? NotFound("Novel project not found.") : Ok(project);
    }

    [HttpDelete("novel-projects/{projectId}")]
    public async Task<IActionResult> Delete(string projectId, CancellationToken ct)
    {
        var result = await _catalog.DeleteAsync(projectId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

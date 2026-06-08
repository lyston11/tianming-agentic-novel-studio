using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class StoryBibleController : ControllerBase
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly NovelProjectCatalog _catalog;

    public StoryBibleController(NovelAgentWorkspace workspace, NovelProjectCatalog catalog)
    {
        _workspace = workspace;
        _catalog = catalog;
    }

    [HttpGet("story-bible")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var active = await _catalog.GetActiveAsync(ct);
        return Ok(await _catalog.WithProjectAsync(active, () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct));
    }
}

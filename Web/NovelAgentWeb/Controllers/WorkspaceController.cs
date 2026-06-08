using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class WorkspaceController : ControllerBase
{
    private readonly NovelAgentWorkspace _workspace;

    public WorkspaceController(NovelAgentWorkspace workspace) => _workspace = workspace;

    [HttpGet("workspace")]
    public IActionResult Get() => Ok(new
    {
        _workspace.ProjectName,
        _workspace.StorageRoot,
        StoryBiblePath = _workspace.StoryBibleService.GetStoragePath(),
        CreativeKnowledgePath = _workspace.CreativeKnowledgeBaseService.GetStoragePath()
    });
}

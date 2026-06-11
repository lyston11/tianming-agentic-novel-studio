using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Workflow;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/workflow")]
[Authorize]
public class WorkflowController : ControllerBase
{
    private readonly IWorkflowService _workflowService;
    private readonly ILogger<WorkflowController> _logger;

    public WorkflowController(
        IWorkflowService workflowService,
        ILogger<WorkflowController> logger)
    {
        _workflowService = workflowService;
        _logger = logger;
    }

    [HttpGet("workspace")]
    public async Task<IActionResult> GetWorkspace(CancellationToken ct)
    {
        try
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var workspace = await _workflowService.GetWorkspaceAsync(userId, ct);
            return Ok(workspace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workspace for user");
            return StatusCode(500, new { error = "Failed to get workspace" });
        }
    }

    [HttpPost("volumes")]
    public async Task<IActionResult> CreateVolumeArc(
        [FromBody] CreateVolumeArcRequest request,
        CancellationToken ct)
    {
        try
        {
            var volumeArc = await _workflowService.CreateVolumeArcAsync(request, ct);
            return Ok(volumeArc);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create volume arc");
            return StatusCode(500, new { error = "Failed to create volume arc" });
        }
    }

    [HttpGet("volumes")]
    public async Task<IActionResult> ListVolumeArcs(
        [FromQuery] string projectId,
        CancellationToken ct)
    {
        try
        {
            var volumeArcs = await _workflowService.ListVolumeArcsAsync(projectId, ct);
            return Ok(volumeArcs);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list volume arcs for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Failed to list volume arcs" });
        }
    }

    [HttpGet("volumes/{id}")]
    public async Task<IActionResult> GetVolumeArc(string id, CancellationToken ct)
    {
        try
        {
            var volumeArc = await _workflowService.GetVolumeArcAsync(id, ct);
            return Ok(volumeArc);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get volume arc {VolumeArcId}", id);
            return StatusCode(500, new { error = "Failed to get volume arc" });
        }
    }

    [HttpPatch("volumes/{id}")]
    public async Task<IActionResult> UpdateVolumeArc(
        string id,
        [FromBody] UpdateVolumeArcRequest request,
        CancellationToken ct)
    {
        try
        {
            var volumeArc = await _workflowService.UpdateVolumeArcAsync(id, request, ct);
            return Ok(volumeArc);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update volume arc {VolumeArcId}", id);
            return StatusCode(500, new { error = "Failed to update volume arc" });
        }
    }

    [HttpDelete("volumes/{id}")]
    public async Task<IActionResult> DeleteVolumeArc(string id, CancellationToken ct)
    {
        try
        {
            await _workflowService.DeleteVolumeArcAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete volume arc {VolumeArcId}", id);
            return StatusCode(500, new { error = "Failed to delete volume arc" });
        }
    }
}

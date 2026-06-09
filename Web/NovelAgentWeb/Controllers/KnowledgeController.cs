using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Knowledge;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/knowledge")]
[Authorize]
public class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly ILogger<KnowledgeController> _logger;

    public KnowledgeController(
        IKnowledgeService knowledgeService,
        ILogger<KnowledgeController> logger)
    {
        _knowledgeService = knowledgeService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateKnowledge(
        [FromBody] CreateKnowledgeRequest request,
        CancellationToken ct)
    {
        try
        {
            var knowledge = await _knowledgeService.CreateKnowledgeAsync(request, ct);
            return Ok(knowledge);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create knowledge entry");
            return StatusCode(500, new { error = "Failed to create knowledge entry" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListKnowledge(
        [FromQuery] string projectId,
        CancellationToken ct)
    {
        try
        {
            var knowledge = await _knowledgeService.ListKnowledgeAsync(projectId, ct);
            return Ok(knowledge);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list knowledge for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Failed to list knowledge" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetKnowledge(string id, CancellationToken ct)
    {
        try
        {
            var knowledge = await _knowledgeService.GetKnowledgeAsync(id, ct);
            return Ok(knowledge);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get knowledge {KnowledgeId}", id);
            return StatusCode(500, new { error = "Failed to get knowledge" });
        }
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateKnowledge(
        string id,
        [FromBody] UpdateKnowledgeRequest request,
        CancellationToken ct)
    {
        try
        {
            var knowledge = await _knowledgeService.UpdateKnowledgeAsync(id, request, ct);
            return Ok(knowledge);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update knowledge {KnowledgeId}", id);
            return StatusCode(500, new { error = "Failed to update knowledge" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteKnowledge(string id, CancellationToken ct)
    {
        try
        {
            await _knowledgeService.DeleteKnowledgeAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete knowledge {KnowledgeId}", id);
            return StatusCode(500, new { error = "Failed to delete knowledge" });
        }
    }

    [HttpPost("search")]
    public async Task<IActionResult> SearchKnowledge(
        [FromBody] SearchKnowledgeRequest request,
        CancellationToken ct)
    {
        try
        {
            var results = await _knowledgeService.SearchKnowledgeAsync(request, ct);
            return Ok(results);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search knowledge in project {ProjectId}", request.ProjectId);
            return StatusCode(500, new { error = "Failed to search knowledge" });
        }
    }

    [HttpPost("{id}/increment-usage")]
    public async Task<IActionResult> IncrementUsage(string id, CancellationToken ct)
    {
        try
        {
            await _knowledgeService.IncrementUsageAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment usage for knowledge {KnowledgeId}", id);
            return StatusCode(500, new { error = "Failed to increment usage" });
        }
    }
}

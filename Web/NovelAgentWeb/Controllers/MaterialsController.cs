using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Materials;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/materials")]
[Authorize]
public class MaterialsController : ControllerBase
{
    private readonly IMaterialService _materialService;
    private readonly ILogger<MaterialsController> _logger;

    public MaterialsController(
        IMaterialService materialService,
        ILogger<MaterialsController> logger)
    {
        _materialService = materialService;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadMaterial(
        [FromForm] UploadMaterialRequest request,
        CancellationToken ct)
    {
        try
        {
            var material = await _materialService.UploadMaterialAsync(request, ct);
            return Ok(material);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload material");
            return StatusCode(500, new { error = "Failed to upload material" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateMaterial(
        [FromBody] CreateMaterialRequest request,
        CancellationToken ct)
    {
        try
        {
            var material = await _materialService.CreateMaterialAsync(request, ct);
            return Ok(material);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create material");
            return StatusCode(500, new { error = "Failed to create material" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListMaterials(
        [FromQuery] string projectId,
        CancellationToken ct)
    {
        try
        {
            var materials = await _materialService.ListMaterialsAsync(projectId, ct);
            return Ok(materials);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list materials for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Failed to list materials" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetMaterial(string id, CancellationToken ct)
    {
        try
        {
            var material = await _materialService.GetMaterialAsync(id, ct);
            return Ok(material);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get material {MaterialId}", id);
            return StatusCode(500, new { error = "Failed to get material" });
        }
    }

    [HttpGet("{id}/content")]
    public async Task<IActionResult> GetMaterialContent(string id, CancellationToken ct)
    {
        try
        {
            var content = await _materialService.GetMaterialContentAsync(id, ct);
            return Ok(content);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get material content {MaterialId}", id);
            return StatusCode(500, new { error = "Failed to get material content" });
        }
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateMaterial(
        string id,
        [FromBody] UpdateMaterialRequest request,
        CancellationToken ct)
    {
        try
        {
            var material = await _materialService.UpdateMaterialAsync(id, request, ct);
            return Ok(material);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update material {MaterialId}", id);
            return StatusCode(500, new { error = "Failed to update material" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMaterial(string id, CancellationToken ct)
    {
        try
        {
            await _materialService.DeleteMaterialAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete material {MaterialId}", id);
            return StatusCode(500, new { error = "Failed to delete material" });
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.StoryBible;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/storybible")]
[Authorize]
public class StoryBibleController : ControllerBase
{
    private readonly IStoryBibleService _storyBibleService;
    private readonly ILogger<StoryBibleController> _logger;

    public StoryBibleController(
        IStoryBibleService storyBibleService,
        ILogger<StoryBibleController> logger)
    {
        _storyBibleService = storyBibleService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetStoryBibleByProject(
        [FromQuery] string projectId,
        CancellationToken ct)
    {
        try
        {
            var storyBible = await _storyBibleService.GetStoryBibleByProjectAsync(projectId, ct);
            return Ok(storyBible);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get story bible for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Failed to get story bible" });
        }
    }

    // Story Constitution endpoints

    [HttpPost("constitution")]
    public async Task<IActionResult> CreateConstitution(
        [FromBody] CreateStoryConstitutionRequest request,
        CancellationToken ct)
    {
        try
        {
            var constitution = await _storyBibleService.CreateConstitutionAsync(request, ct);
            return Ok(constitution);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create story constitution");
            return StatusCode(500, new { error = "Failed to create story constitution" });
        }
    }

    [HttpGet("constitution")]
    public async Task<IActionResult> GetConstitutionByProject(
        [FromQuery] string projectId,
        CancellationToken ct)
    {
        try
        {
            var constitution = await _storyBibleService.GetConstitutionByProjectAsync(projectId, ct);
            if (constitution == null)
                return NotFound(new { error = "Story constitution not found" });
            return Ok(constitution);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get story constitution for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Failed to get story constitution" });
        }
    }

    [HttpPatch("constitution/{id}")]
    public async Task<IActionResult> UpdateConstitution(
        string id,
        [FromBody] UpdateStoryConstitutionRequest request,
        CancellationToken ct)
    {
        try
        {
            var constitution = await _storyBibleService.UpdateConstitutionAsync(id, request, ct);
            return Ok(constitution);
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
            _logger.LogError(ex, "Failed to update story constitution {ConstitutionId}", id);
            return StatusCode(500, new { error = "Failed to update story constitution" });
        }
    }

    [HttpDelete("constitution/{id}")]
    public async Task<IActionResult> DeleteConstitution(string id, CancellationToken ct)
    {
        try
        {
            await _storyBibleService.DeleteConstitutionAsync(id, ct);
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
            _logger.LogError(ex, "Failed to delete story constitution {ConstitutionId}", id);
            return StatusCode(500, new { error = "Failed to delete story constitution" });
        }
    }

    // Character endpoints

    [HttpPost("characters")]
    public async Task<IActionResult> CreateCharacter(
        [FromBody] CreateCharacterRequest request,
        CancellationToken ct)
    {
        try
        {
            var character = await _storyBibleService.CreateCharacterAsync(request, ct);
            return Ok(character);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create character");
            return StatusCode(500, new { error = "Failed to create character" });
        }
    }

    [HttpGet("characters")]
    public async Task<IActionResult> ListCharacters(
        [FromQuery] string projectId,
        CancellationToken ct)
    {
        try
        {
            var characters = await _storyBibleService.ListCharactersAsync(projectId, ct);
            return Ok(characters);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list characters for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Failed to list characters" });
        }
    }

    [HttpGet("characters/{id}")]
    public async Task<IActionResult> GetCharacter(string id, CancellationToken ct)
    {
        try
        {
            var character = await _storyBibleService.GetCharacterAsync(id, ct);
            return Ok(character);
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
            _logger.LogError(ex, "Failed to get character {CharacterId}", id);
            return StatusCode(500, new { error = "Failed to get character" });
        }
    }

    [HttpPatch("characters/{id}")]
    public async Task<IActionResult> UpdateCharacter(
        string id,
        [FromBody] UpdateCharacterRequest request,
        CancellationToken ct)
    {
        try
        {
            var character = await _storyBibleService.UpdateCharacterAsync(id, request, ct);
            return Ok(character);
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
            _logger.LogError(ex, "Failed to update character {CharacterId}", id);
            return StatusCode(500, new { error = "Failed to update character" });
        }
    }

    [HttpDelete("characters/{id}")]
    public async Task<IActionResult> DeleteCharacter(string id, CancellationToken ct)
    {
        try
        {
            await _storyBibleService.DeleteCharacterAsync(id, ct);
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
            _logger.LogError(ex, "Failed to delete character {CharacterId}", id);
            return StatusCode(500, new { error = "Failed to delete character" });
        }
    }
}

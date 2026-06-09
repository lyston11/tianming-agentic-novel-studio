using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Chapters;

namespace TM.Web.NovelAgentWeb.Controllers;

/// <summary>
/// API controller for chapter CRUD operations with synchronization across
/// SQLite (metadata), file system (Markdown content), and Qdrant (vectors).
/// All endpoints require JWT authentication.
/// </summary>
[ApiController]
[Route("api/chapters")]
[Authorize]
public class ChaptersController : ControllerBase
{
    private readonly IChapterService _chapterService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ChaptersController> _logger;

    public ChaptersController(
        IChapterService chapterService,
        ICurrentUserService currentUserService,
        ILogger<ChaptersController> logger)
    {
        _chapterService = chapterService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new chapter with atomic synchronization across database, file system, and Qdrant.
    /// </summary>
    /// <param name="request">Chapter creation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created chapter with content</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ChapterResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateChapter(
        [FromBody] CreateChapterRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            var chapter = await _chapterService.CreateChapterAsync(
                request,
                userId,
                isAdmin,
                cancellationToken);

            _logger.LogInformation("User {UserId} created chapter {ChapterId} in project {ProjectId}",
                userId, chapter.Id, request.ProjectId);

            return CreatedAtAction(
                nameof(GetChapterById),
                new { id = chapter.Id },
                chapter);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create chapter in project {ProjectId}", request.ProjectId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while creating the chapter" });
        }
    }

    /// <summary>
    /// Update an existing chapter with atomic synchronization.
    /// If content is updated, embeddings are regenerated.
    /// </summary>
    /// <param name="id">Chapter ID</param>
    /// <param name="request">Chapter update request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated chapter with content</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ChapterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateChapter(
        string id,
        [FromBody] UpdateChapterRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            var chapter = await _chapterService.UpdateChapterAsync(
                id,
                request,
                userId,
                isAdmin,
                cancellationToken);

            _logger.LogInformation("User {UserId} updated chapter {ChapterId}", userId, id);

            return Ok(chapter);
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
            _logger.LogError(ex, "Failed to update chapter {ChapterId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while updating the chapter" });
        }
    }

    /// <summary>
    /// Get a chapter by ID with content.
    /// </summary>
    /// <param name="id">Chapter ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chapter with content</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ChapterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChapterById(
        string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            var chapter = await _chapterService.GetChapterByIdAsync(
                id,
                userId,
                isAdmin,
                cancellationToken);

            return Ok(chapter);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "Chapter content file not found for chapter {ChapterId}", id);
            return NotFound(new { error = "Chapter content file not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chapter {ChapterId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while retrieving the chapter" });
        }
    }

    /// <summary>
    /// Delete a chapter with atomic synchronization.
    /// Removes metadata from database, content file, and vectors from Qdrant.
    /// Foreign key references (foreshadows) are automatically set to NULL.
    /// </summary>
    /// <param name="id">Chapter ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteChapter(
        string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            await _chapterService.DeleteChapterAsync(
                id,
                userId,
                isAdmin,
                cancellationToken);

            _logger.LogInformation("User {UserId} deleted chapter {ChapterId}", userId, id);

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
            _logger.LogError(ex, "Failed to delete chapter {ChapterId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while deleting the chapter" });
        }
    }

    /// <summary>
    /// Get all chapters for a project (without content).
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of chapters without content</returns>
    [HttpGet("project/{projectId}")]
    [ProducesResponseType(typeof(List<ChapterResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChaptersByProject(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            var chapters = await _chapterService.GetChaptersByProjectAsync(
                projectId,
                userId,
                isAdmin,
                cancellationToken);

            return Ok(chapters);
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
            _logger.LogError(ex, "Failed to get chapters for project {ProjectId}", projectId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while retrieving chapters" });
        }
    }
}

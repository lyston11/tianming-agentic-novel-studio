using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Chapters;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Controllers;

/// <summary>
/// API controller for chapter CRUD operations backed by database content documents
/// and asynchronous production outbox indexing.
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
    private readonly IChapterVersionRollbackService? _rollbackService;

    public ChaptersController(
        IChapterService chapterService,
        ICurrentUserService currentUserService,
        ILogger<ChaptersController> logger,
        IChapterVersionRollbackService? rollbackService = null)
    {
        _chapterService = chapterService;
        _currentUserService = currentUserService;
        _logger = logger;
        _rollbackService = rollbackService;
    }

    /// <summary>
    /// Create a new chapter in the database and enqueue asynchronous indexing.
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
            request.IdempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey)
                ? idempotencyKey.ToString()
                : string.Empty;

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
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiErrors.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create chapter in project {ProjectId}", request.ProjectId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiErrors.Internal("An error occurred while creating the chapter"));
        }
    }

    /// <summary>
    /// Update an existing chapter and enqueue any required index refresh.
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
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update chapter {ChapterId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiErrors.Internal("An error occurred while updating the chapter"));
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
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chapter {ChapterId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiErrors.Internal("An error occurred while retrieving the chapter"));
        }
    }

    /// <summary>
    /// Get all persisted versions for a chapter.
    /// </summary>
    /// <param name="id">Chapter ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chapter versions with production lineage</returns>
    [HttpGet("{id}/versions")]
    [ProducesResponseType(typeof(List<ChapterVersionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChapterVersions(
        string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            var versions = await _chapterService.GetChapterVersionsAsync(
                id,
                userId,
                isAdmin,
                cancellationToken);

            return Ok(versions);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chapter versions for {ChapterId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiErrors.Internal("An error occurred while retrieving chapter versions"));
        }
    }

    /// <summary>
    /// Compare two persisted versions for a chapter.
    /// </summary>
    /// <param name="id">Chapter ID</param>
    /// <param name="leftVersionId">Baseline version ID</param>
    /// <param name="rightVersionId">Target version ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paragraph-level version diff with production lineage</returns>
    [HttpGet("{id}/versions/compare")]
    [ProducesResponseType(typeof(ChapterVersionCompareResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompareChapterVersions(
        string id,
        [FromQuery] string leftVersionId,
        [FromQuery] string rightVersionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            var comparison = await _chapterService.CompareChapterVersionsAsync(
                id,
                leftVersionId,
                rightVersionId,
                userId,
                isAdmin,
                cancellationToken);

            return Ok(comparison);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrors.BadRequest(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare chapter versions for {ChapterId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiErrors.Internal("An error occurred while comparing chapter versions"));
        }
    }

    /// <summary>
    /// Roll back a committed chapter to a previous persisted version.
    /// </summary>
    /// <param name="id">Chapter ID</param>
    /// <param name="versionId">Target chapter version ID</param>
    /// <param name="request">Rollback reason</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rollback result with invalidated package IDs</returns>
    [HttpPost("{id}/versions/{versionId}/rollback")]
    [ProducesResponseType(typeof(RollbackChapterVersionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RollbackChapterVersion(
        string id,
        string versionId,
        [FromBody] RollbackChapterVersionApiRequest? request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_rollbackService == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiErrors.Internal("Chapter version rollback service is not configured"));
            }

            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();
            var chapter = await _chapterService.GetChapterByIdAsync(
                id,
                userId,
                isAdmin,
                cancellationToken);

            var result = await _rollbackService.RollbackAsync(
                new RollbackChapterVersionRequest(
                    UserId: userId,
                    ProjectId: chapter.ProjectId,
                    ChapterId: id,
                    TargetVersionId: versionId,
                    RuntimeRunId: $"library-rollback:{versionId}",
                    Reason: request?.Reason,
                    IdempotencyKey: Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey)
                        ? idempotencyKey.ToString()
                        : string.Empty),
                cancellationToken);

            if (!result.Success)
            {
                return BadRequest(ApiErrors.BadRequest(result.Message));
            }

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to roll back chapter {ChapterId} to version {VersionId}", id, versionId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiErrors.Internal("An error occurred while rolling back chapter version"));
        }
    }

    /// <summary>
    /// Delete a chapter and enqueue asynchronous index cleanup.
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
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete chapter {ChapterId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiErrors.Internal("An error occurred while deleting the chapter"));
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
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chapters for project {ProjectId}", projectId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiErrors.Internal("An error occurred while retrieving chapters"));
        }
    }
}

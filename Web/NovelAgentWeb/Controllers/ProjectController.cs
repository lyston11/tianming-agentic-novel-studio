using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Models.Projects;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Projects;

namespace TM.Web.NovelAgentWeb.Controllers;

/// <summary>
/// API controller for novel project management with user isolation and authorization.
/// All endpoints require JWT authentication.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ProjectController> _logger;

    public ProjectController(
        IProjectService projectService,
        ICurrentUserService currentUserService,
        ILogger<ProjectController> logger)
    {
        _projectService = projectService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    /// <summary>
    /// Get paginated list of projects for the current user.
    /// Regular users see only their own projects; admins see all projects.
    /// </summary>
    /// <param name="pageNumber">Page number (1-based, default: 1)</param>
    /// <param name="pageSize">Page size (default: 20, max: 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged response with project list</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProjects(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();
        var isAdmin = _currentUserService.IsAdmin();

        var result = await _projectService.GetUserProjectsAsync(
            userId,
            isAdmin,
            pageNumber,
            pageSize,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Get a single project by ID with ownership verification.
    /// </summary>
    /// <param name="id">Project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Project details</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProject(
        string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            var project = await _projectService.GetProjectByIdAsync(
                id,
                userId,
                isAdmin,
                cancellationToken);

            return Ok(project);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Project {ProjectId} not found", id);
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access to project {ProjectId}", id);
            return Forbid();
        }
    }

    /// <summary>
    /// Create a new project for the current user.
    /// </summary>
    /// <param name="request">Project creation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created project</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateProject(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = _currentUserService.GetUserId();

        var project = await _projectService.CreateProjectAsync(
            request,
            userId,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetProject),
            new { id = project.Id },
            project);
    }

    /// <summary>
    /// Update an existing project with ownership verification.
    /// </summary>
    /// <param name="id">Project ID</param>
    /// <param name="request">Project update request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated project</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProject(
        string id,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            var project = await _projectService.UpdateProjectAsync(
                id,
                request,
                userId,
                isAdmin,
                cancellationToken);

            return Ok(project);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Project {ProjectId} not found", id);
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized update to project {ProjectId}", id);
            return Forbid();
        }
    }

    /// <summary>
    /// Delete a project with ownership verification.
    /// Cascades to chapters, foreshadows, and Qdrant collection.
    /// </summary>
    /// <param name="id">Project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content on success</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProject(
        string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            await _projectService.DeleteProjectAsync(
                id,
                userId,
                isAdmin,
                cancellationToken);

            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Project {ProjectId} not found", id);
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized deletion of project {ProjectId}", id);
            return Forbid();
        }
    }
}

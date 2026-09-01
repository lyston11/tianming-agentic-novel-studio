using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Controllers;

/// <summary>
/// Admin-only controller for workspace monitoring and diagnostics.
/// Provides cache statistics, violation tracking, and health checks.
/// </summary>
[ApiController]
[Route("api/admin/workspace")]
[Authorize(Roles = "Admin")]
public class WorkspaceMonitoringController : ControllerBase
{
    private readonly IWorkspaceFactory _workspaceFactory;
    private readonly IWorkspaceViolationTracker _violationTracker;
    private readonly ILogger<WorkspaceMonitoringController> _logger;

    public WorkspaceMonitoringController(
        IWorkspaceFactory workspaceFactory,
        IWorkspaceViolationTracker violationTracker,
        ILogger<WorkspaceMonitoringController> logger)
    {
        _workspaceFactory = workspaceFactory;
        _violationTracker = violationTracker;
        _logger = logger;
    }

    /// <summary>
    /// Get current workspace cache statistics including hit rate, evictions, and active count.
    /// </summary>
    /// <returns>Cache statistics</returns>
    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetStats()
    {
        try
        {
            var stats = _workspaceFactory.GetDetailedStats();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve workspace statistics");
            return StatusCode(500, ApiErrors.Internal("Failed to retrieve statistics"));
        }
    }

    /// <summary>
    /// Query workspace violations with optional filters.
    /// </summary>
    /// <param name="severity">Filter by violation severity (Critical, Warning, Info)</param>
    /// <param name="since">Filter violations since this timestamp</param>
    /// <param name="userId">Filter by user ID</param>
    /// <param name="projectId">Filter by project ID</param>
    /// <param name="limit">Maximum number of violations to return (default: 100)</param>
    /// <returns>List of violations matching the criteria</returns>
    [HttpGet("violations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetViolations(
        [FromQuery] ViolationSeverity? severity = null,
        [FromQuery] DateTime? since = null,
        [FromQuery] string? userId = null,
        [FromQuery] string? projectId = null,
        [FromQuery] int limit = 100)
    {
        try
        {
            var violations = _violationTracker.GetViolations(severity, since, userId, projectId)
                .Take(limit)
                .ToList();

            return Ok(new
            {
                count = violations.Count,
                violations = violations
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve violations");
            return StatusCode(500, ApiErrors.Internal("Failed to retrieve violations"));
        }
    }

    /// <summary>
    /// Get detailed information about a specific workspace.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="projectId">Project ID</param>
    /// <returns>Workspace detail including active status and cache metadata</returns>
    [HttpGet("{userId}/{projectId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetWorkspaceDetail(string userId, string projectId)
    {
        try
        {
            var isActive = _workspaceFactory.IsWorkspaceActive(userId, projectId);

            if (!isActive)
            {
                return NotFound(ApiErrors.NotFound(
                    "Workspace not found in cache",
                    artifactIds: new[] { userId, projectId }));
            }

            return Ok(new
            {
                userId = userId,
                projectId = projectId,
                isActive = true,
                message = "Workspace is currently active in cache"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve workspace detail for user {UserId}, project {ProjectId}", userId, projectId);
            return StatusCode(500, ApiErrors.Internal("Failed to retrieve workspace detail"));
        }
    }

    /// <summary>
    /// Perform health diagnostics on the workspace factory.
    /// Returns 200 if healthy, 503 if unhealthy.
    /// </summary>
    /// <returns>Health status with diagnostic information</returns>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetHealth()
    {
        try
        {
            var healthStatus = await _workspaceFactory.CheckHealthAsync();

            if (healthStatus.IsHealthy)
            {
                return Ok(healthStatus);
            }

            return StatusCode(503, healthStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check workspace factory health");
            return StatusCode(500, ApiErrors.Internal("Health check failed with exception"));
        }
    }

    /// <summary>
    /// Force release and evict a workspace from cache.
    /// Used to clean up zombie resources.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="projectId">Project ID</param>
    /// <returns>Result of force release operation</returns>
    [HttpPost("users/{userId}/projects/{projectId}/force-release")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult ForceRelease(string userId, string projectId)
    {
        try
        {
            var status = _workspaceFactory.GetWorkspaceStatus(userId, projectId);

            if (status == WorkspaceStatus.NotFound)
            {
                return NotFound(ApiErrors.NotFound(
                    "Workspace not found in cache",
                    artifactIds: new[] { userId, projectId }));
            }

            _workspaceFactory.ForceRelease(userId, projectId);

            _logger.LogInformation(
                "Force released workspace for user {UserId}, project {ProjectId}",
                userId,
                projectId);

            return Ok(new
            {
                message = "Workspace force released successfully",
                userId = userId,
                projectId = projectId,
                previousStatus = status.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to force release workspace for user {UserId}, project {ProjectId}", userId, projectId);
            return StatusCode(500, ApiErrors.Internal("Failed to force release workspace"));
        }
    }

    /// <summary>
    /// Reset violations for a specific user/project combination.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="projectId">Project ID</param>
    /// <returns>Result of reset operation</returns>
    [HttpPost("violations/{userId}/{projectId}/reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult ResetViolations(string userId, string projectId)
    {
        try
        {
            // Clear violations for all types for this user/project
            foreach (ViolationType type in Enum.GetValues(typeof(ViolationType)))
            {
                var key = $"{userId}:{projectId}:{type}";
                _violationTracker.ClearViolation(key);
            }

            _logger.LogInformation(
                "Reset violations for user {UserId}, project {ProjectId}",
                userId,
                projectId);

            return Ok(new
            {
                message = "Violations reset successfully",
                userId = userId,
                projectId = projectId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset violations for user {UserId}, project {ProjectId}", userId, projectId);
            return StatusCode(500, ApiErrors.Internal("Failed to reset violations"));
        }
    }

    /// <summary>
    /// Clear all workspaces from cache.
    /// Used for emergency maintenance without restarting the service.
    /// </summary>
    /// <returns>Result of cache clear operation</returns>
    [HttpPost("cache/clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult ClearCache()
    {
        try
        {
            var statsBefore = _workspaceFactory.GetDetailedStats();
            _workspaceFactory.ClearAll();

            _logger.LogWarning("All workspaces cleared from cache by admin");

            return Ok(new
            {
                message = "Cache cleared successfully",
                workspacesCleared = statsBefore.ActiveWorkspacesCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear cache");
            return StatusCode(500, ApiErrors.Internal("Failed to clear cache"));
        }
    }

    /// <summary>
    /// Get detailed status of a specific workspace (NotFound, Active, Idle, Evicting).
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="projectId">Project ID</param>
    /// <returns>Workspace status</returns>
    [HttpGet("users/{userId}/projects/{projectId}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetWorkspaceStatus(string userId, string projectId)
    {
        try
        {
            var status = _workspaceFactory.GetWorkspaceStatus(userId, projectId);

            return Ok(new
            {
                userId = userId,
                projectId = projectId,
                status = status.ToString(),
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workspace status for user {UserId}, project {ProjectId}", userId, projectId);
            return StatusCode(500, ApiErrors.Internal("Failed to get workspace status"));
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            return StatusCode(500, new { message = "Failed to retrieve statistics", error = ex.Message });
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
            return StatusCode(500, new { message = "Failed to retrieve violations", error = ex.Message });
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
                return NotFound(new
                {
                    message = "Workspace not found in cache",
                    userId = userId,
                    projectId = projectId
                });
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
            return StatusCode(500, new { message = "Failed to retrieve workspace detail", error = ex.Message });
        }
    }

    /// <summary>
    /// Invalidate a specific workspace, forcing it to be evicted from cache.
    /// The workspace will be reloaded on next access.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="projectId">Project ID</param>
    /// <returns>Result of invalidation operation</returns>
    [HttpPost("{userId}/{projectId}/invalidate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult InvalidateWorkspace(string userId, string projectId)
    {
        try
        {
            var isActive = _workspaceFactory.IsWorkspaceActive(userId, projectId);

            if (!isActive)
            {
                return NotFound(new
                {
                    message = "Workspace not found in cache",
                    userId = userId,
                    projectId = projectId
                });
            }

            // Note: The current IWorkspaceFactory interface doesn't expose an explicit invalidation method.
            // This would typically call something like _workspaceFactory.Invalidate(userId, projectId).
            // For now, we acknowledge the limitation and log it.
            _logger.LogWarning(
                "Invalidation requested for workspace {UserId}/{ProjectId} but explicit invalidation is not yet implemented",
                userId,
                projectId);

            return Ok(new
            {
                message = "Workspace invalidation logged (explicit eviction not yet implemented)",
                userId = userId,
                projectId = projectId,
                note = "Workspace will be evicted naturally based on LRU policy"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate workspace for user {UserId}, project {ProjectId}", userId, projectId);
            return StatusCode(500, new { message = "Failed to invalidate workspace", error = ex.Message });
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
            return StatusCode(500, new
            {
                isHealthy = false,
                message = "Health check failed with exception",
                error = ex.Message,
                lastCheckTimestamp = DateTime.UtcNow
            });
        }
    }
}

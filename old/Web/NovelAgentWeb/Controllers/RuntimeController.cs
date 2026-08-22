using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/runtime")]
[Authorize]
public sealed class RuntimeController : ControllerBase
{
    private readonly ILegacyRuntimeAuditReader _runs;
    private readonly IAgentRuntimeEventService _events;
    private readonly ICurrentUserService _currentUser;

    public RuntimeController(
        ILegacyRuntimeAuditReader runs,
        IAgentRuntimeEventService events,
        ICurrentUserService currentUser)
    {
        _runs = runs;
        _events = events;
        _currentUser = currentUser;
    }

    [HttpGet("runs/{runId}")]
    public async Task<IActionResult> GetRun(string runId, CancellationToken ct)
    {
        var run = await _runs.TryGetAsync(runId, ct).ConfigureAwait(false);
        if (run == null)
        {
            return NotFound(Error("RUN_NOT_FOUND", "Runtime run not found.", recoverable: false));
        }

        if (!CanRead(run.UserId))
            return AccessDenied();

        return Ok(RuntimeRunDto.FromEntity(run));
    }

    [HttpGet("sessions/{sessionId}/active-run")]
    public async Task<IActionResult> GetActiveRun(
        string sessionId,
        [FromQuery] string? userId = null,
        CancellationToken ct = default)
    {
        var targetUserId = ResolveReadableUserId(userId);
        if (targetUserId == null)
            return AccessDenied();

        var state = await _runs.TryGetActiveStateAsync(targetUserId, sessionId, ct).ConfigureAwait(false);
        return Ok(state == null
            ? RuntimeActiveRunDto.Empty()
            : RuntimeActiveRunDto.FromState(state));
    }

    [HttpGet("runs/{runId}/events")]
    public async Task<IActionResult> GetRunEvents(
        string runId,
        [FromQuery] int limit = 100,
        [FromQuery] string? afterEventId = null,
        CancellationToken ct = default)
    {
        var run = await _runs.TryGetAsync(runId, ct).ConfigureAwait(false);
        if (run == null)
        {
            return NotFound(Error("RUN_NOT_FOUND", "Runtime run not found.", recoverable: false));
        }

        if (!CanRead(run.UserId))
            return AccessDenied();

        var events = await _events.GetForRunAsync(runId, limit, ct, afterEventId).ConfigureAwait(false);
        var payload = events.Select(RuntimeEventDto.FromEntity).ToList();
        return Ok(payload);
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetRunEvents(
        [FromQuery] string? runId,
        [FromQuery] string? userId,
        [FromQuery] string? sessionId,
        [FromQuery] int limit = 100,
        [FromQuery] string? afterEventId = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(runId))
            return await GetRunEvents(runId, limit, afterEventId, ct).ConfigureAwait(false);

        var targetUserId = ResolveReadableUserId(userId);
        if (targetUserId == null)
            return AccessDenied();

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(Error(
                "INVALID_REQUEST",
                "runId 或 sessionId 至少需要提供一组。",
                recoverable: true,
                recommendedAction: "QueryRuntimeRun"));
        }

        var events = await _events.GetRecentAsync(targetUserId, sessionId, limit, ct, afterEventId).ConfigureAwait(false);
        var payload = events.Select(RuntimeEventDto.FromEntity).ToList();
        return Ok(payload);
    }

    private string? ResolveReadableUserId(string? requestedUserId)
    {
        var currentUserId = _currentUser.GetUserId();
        var normalizedRequested = string.IsNullOrWhiteSpace(requestedUserId)
            ? currentUserId
            : requestedUserId.Trim();

        if (_currentUser.IsAdmin())
            return normalizedRequested;

        return string.Equals(currentUserId, normalizedRequested, StringComparison.Ordinal)
            ? normalizedRequested
            : null;
    }

    private bool CanRead(string ownerUserId)
    {
        if (_currentUser.IsAdmin())
            return true;

        var userId = _currentUser.GetUserId();
        return string.Equals(userId, ownerUserId, StringComparison.Ordinal);
    }

    private ObjectResult AccessDenied() =>
        StatusCode(StatusCodes.Status403Forbidden, Error(
            "RUN_ACCESS_DENIED",
            "无权访问该运行任务。",
            recoverable: false));

    private static object Error(
        string code,
        string message,
        bool recoverable,
        string recommendedAction = "") =>
        new { code, message, recoverable, recommendedAction };

}

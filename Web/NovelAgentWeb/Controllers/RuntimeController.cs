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
    private readonly IAgentRuntimeRunService _runs;
    private readonly IAgentRuntimeEventService _events;
    private readonly ICurrentUserService _currentUser;
    private readonly IAgentInterruptService? _interrupts;

    public RuntimeController(
        IAgentRuntimeRunService runs,
        IAgentRuntimeEventService events,
        ICurrentUserService currentUser,
        IAgentInterruptService? interrupts = null)
    {
        _runs = runs;
        _events = events;
        _currentUser = currentUser;
        _interrupts = interrupts;
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

    [HttpPost("runs/{runId}/cancel")]
    public async Task<IActionResult> CancelRun(string runId, CancellationToken ct)
    {
        try
        {
            var existing = await _runs.TryGetAsync(runId, ct).ConfigureAwait(false);
            if (existing == null)
            {
                return NotFound(Error("RUN_NOT_FOUND", "Runtime run not found.", recoverable: false));
            }

            if (!CanRead(existing.UserId))
                return AccessDenied();

            var run = await _runs.RequestCancelAsync(runId, ct).ConfigureAwait(false);
            if (_interrupts != null)
            {
                await _interrupts.AddAsync(new CreateAgentInterruptRequest(
                        run.Id,
                        run.UserId,
                        run.SessionId,
                        run.ProjectId,
                        "cancel",
                        "用户通过运行控制接口请求取消本次后台执行。",
                        100),
                    ct).ConfigureAwait(false);
            }

            return Ok(RuntimeRunDto.FromEntity(run));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Error("RUN_NOT_FOUND", "Runtime run not found.", recoverable: false));
        }
    }

    [HttpPost("runs/{runId}/interrupt")]
    public async Task<IActionResult> InterruptRun(
        string runId,
        [FromBody] RuntimeInterruptRequest request,
        CancellationToken ct)
    {
        if (_interrupts == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error(
                "INTERRUPT_SERVICE_UNAVAILABLE",
                "运行中补充要求服务不可用。",
                recoverable: true,
                recommendedAction: "Retry"));
        }

        var existing = await _runs.TryGetAsync(runId, ct).ConfigureAwait(false);
        if (existing == null)
        {
            return NotFound(Error("RUN_NOT_FOUND", "Runtime run not found.", recoverable: false));
        }

        if (!CanRead(existing.UserId))
            return AccessDenied();

        if (!AgentRuntimeRunStatus.Active.Contains(existing.Status))
        {
            return BadRequest(Error(
                "RUN_NOT_ACTIVE",
                "后台任务已经结束，不能再追加运行中补充要求。",
                recoverable: true,
                recommendedAction: "QueryRuntimeRun"));
        }

        var message = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return BadRequest(Error(
                "INVALID_INTERRUPT",
                "补充要求不能为空。",
                recoverable: true,
                recommendedAction: "ProvideMessage"));
        }

        var kind = NormalizeInterruptKind(request.Kind);
        if (string.Equals(kind, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            existing = await _runs.RequestCancelAsync(existing.Id, ct).ConfigureAwait(false);
        }

        var interrupt = await _interrupts.AddAsync(new CreateAgentInterruptRequest(
                existing.Id,
                existing.UserId,
                existing.SessionId,
                existing.ProjectId,
                kind,
                message,
                Math.Clamp(request.Priority, 0, 100)),
            ct).ConfigureAwait(false);

        return Ok(RuntimeInterruptDto.FromEntity(interrupt));
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

    private static string NormalizeInterruptKind(string? kind) =>
        kind?.Trim().ToLowerInvariant() switch
        {
            "soft_requirement" => "soft_requirement",
            "direction_change" => "direction_change",
            "cancel" => "cancel",
            "freeform" => "freeform",
            _ => "freeform"
        };
}

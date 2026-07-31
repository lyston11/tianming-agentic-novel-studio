using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class AgentController : ControllerBase
{
    private readonly AgentTurnCoordinator _coordinator;
    private readonly AgentSessionManager _sessionManager;
    private readonly IAgentSessionService _agentSessionService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAgentSessionResumeService _resumeService;
    private readonly IAgentRuntimeEventFanout? _runtimeEventFanout;
    private readonly IAgentRuntimeEventService? _runtimeEvents;

    public AgentController(
        AgentTurnCoordinator coordinator,
        AgentSessionManager sessionManager,
        IAgentSessionService agentSessionService,
        ICurrentUserService currentUserService,
        IAgentSessionResumeService resumeService,
        IAgentRuntimeEventFanout? runtimeEventFanout = null,
        IAgentRuntimeEventService? runtimeEvents = null)
    {
        _coordinator = coordinator;
        _sessionManager = sessionManager;
        _agentSessionService = agentSessionService;
        _currentUserService = currentUserService;
        _resumeService = resumeService;
        _runtimeEventFanout = runtimeEventFanout;
        _runtimeEvents = runtimeEvents;
    }

    [HttpPost("agent/chat")]
    public async Task<IActionResult> Chat([FromBody] AgentChatRequest request, CancellationToken ct)
    {
        var sessionId = request.SessionId;
        var idempotencyKey = ControllerContext.HttpContext?.Request.Headers["Idempotency-Key"].ToString();
        var response = await _coordinator.HandleAsync(
            sessionId,
            request.Message ?? "",
            ct,
            idempotencyKey,
            request.ClientMessageId);
        var payload = AgentChatResponsePublicProjection.ToPublic(response);
        return Ok(payload);
    }

    [HttpGet("agent/sse/{sessionId}")]
    public async Task StreamEvents(string sessionId, [FromQuery] string? afterEventId, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var isAdmin = _currentUserService.IsAdmin();
        try
        {
            await _agentSessionService.GetSessionByIdAsync(sessionId, userId, isAdmin, ct)
                .ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var replayAfterEventId = FirstNonEmpty(afterEventId, Request.Headers["Last-Event-ID"].ToString());
        var isReplay = !string.IsNullOrWhiteSpace(replayAfterEventId);
        await using var subscription = _sessionManager.SubscribeEvents(userId, sessionId, includeBacklog: !isReplay);
        var seenEventIds = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(replayAfterEventId))
        {
            var replayed = await ReplayRuntimeEventsAsync(userId, sessionId, replayAfterEventId, 100, ct)
                .ConfigureAwait(false);
            foreach (var evt in replayed)
            {
                if (!string.IsNullOrWhiteSpace(evt.EventId))
                    seenEventIds.Add(evt.EventId);
                await WriteSseEventAsync(evt, ct).ConfigureAwait(false);
            }
        }

        var reader = subscription.Reader;
        try
        {
            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                if (!string.IsNullOrWhiteSpace(evt.EventId) && !seenEventIds.Add(evt.EventId))
                    continue;
                await WriteSseEventAsync(evt, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
    }

    private async Task WriteSseEventAsync(AgentSseEvent evt, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(ToPublicSseEvent(evt), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        if (!string.IsNullOrWhiteSpace(evt.EventId))
            await Response.WriteAsync($"id: {evt.EventId}\n", ct);
        await Response.WriteAsync($"data: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private async Task<IReadOnlyList<AgentSseEvent>> ReplayRuntimeEventsAsync(
        string userId,
        string sessionId,
        string afterEventId,
        int limit,
        CancellationToken ct)
    {
        var merged = new List<AgentSseEvent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (_runtimeEventFanout != null)
        {
            var redisEvents = await _runtimeEventFanout.ReplayAsync(userId, sessionId, afterEventId, limit, ct)
                .ConfigureAwait(false);
            foreach (var evt in redisEvents)
                AddReplayEvent(merged, seen, evt);
        }

        if (_runtimeEvents != null && merged.Count < limit)
        {
            var sqliteEvents = await _runtimeEvents
                .GetRecentAsync(userId, sessionId, limit, ct, afterEventId)
                .ConfigureAwait(false);
            foreach (var evt in sqliteEvents)
                AddReplayEvent(merged, seen, ToSseEvent(evt));
        }

        return merged.Take(limit).ToList();
    }

    private static void AddReplayEvent(ICollection<AgentSseEvent> events, ISet<string> seen, AgentSseEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.EventId) && !seen.Add(evt.EventId))
            return;

        events.Add(evt);
    }

    private static AgentSseEvent ToSseEvent(TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent evt)
    {
        object? data = null;
        if (!string.IsNullOrWhiteSpace(evt.DataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(evt.DataJson);
                data = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                data = null;
            }
        }

        return new AgentSseEvent
        {
            EventId = evt.Id,
            Type = evt.Type,
            SessionId = evt.SessionId,
            RunId = evt.RuntimeRunId,
            SourceMessageId = ExtractSourceMessageId(data),
            Stage = evt.Stage,
            Status = evt.Status,
            ArtifactType = evt.ArtifactType,
            ArtifactId = evt.ArtifactId,
            DisplaySurface = evt.DisplaySurface,
            DisplayPolicy = evt.DisplayPolicy,
            Message = evt.Message,
            Data = data,
            Timestamp = evt.CreatedAt
        };
    }

    private static AgentSseEvent ToPublicSseEvent(AgentSseEvent evt)
    {
        if (evt.Type != AgentSseEventType.AgentReply || evt.Data is not AgentChatResponse response)
            return evt;

        return new AgentSseEvent
        {
            EventId = evt.EventId,
            Type = evt.Type,
            SessionId = evt.SessionId,
            RunId = evt.RunId,
            SourceMessageId = evt.SourceMessageId,
            StepId = evt.StepId,
            Stage = evt.Stage,
            Status = evt.Status,
            ArtifactType = evt.ArtifactType,
            ArtifactId = evt.ArtifactId,
            DisplaySurface = evt.DisplaySurface,
            DisplayPolicy = evt.DisplayPolicy,
            Message = evt.Message,
            Data = AgentChatResponsePublicProjection.ToPublic(response),
            Timestamp = evt.Timestamp
        };
    }

    private static string ExtractSourceMessageId(object? data)
    {
        if (data is not JsonElement element || element.ValueKind != JsonValueKind.Object)
            return string.Empty;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, "sourceMessageId", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    [HttpGet("agent/session/{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId, CancellationToken ct)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();
            var session = await _agentSessionService.GetSessionByIdAsync(sessionId, userId, isAdmin, ct);
            return Ok(session);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Error("SESSION_NOT_FOUND", "会话不存在。", recoverable: false));
        }
    }

    [HttpGet("agent/sessions")]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var isAdmin = _currentUserService.IsAdmin();

        var sessions = await _agentSessionService.ListUserSessionsAsync(userId, isAdmin, includeArchived: false, ct);
        return Ok(sessions);
    }

    [HttpGet("agent/sessions/{sessionId}/resume")]
    public async Task<IActionResult> ResumeSession(string sessionId, CancellationToken ct)
    {
        try
        {
            var session = await _resumeService.ResumeAsync(sessionId, ct);
            return Ok(session);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Error("SESSION_NOT_FOUND", "会话不存在。", recoverable: false));
        }
    }

    [HttpPost("agent/session")]
    public async Task<IActionResult> CreateSession([FromQuery] string? projectId, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var header)
            ? header.ToString()
            : string.Empty;
        var session = await _agentSessionService.GetOrCreateSessionAsync(null, userId, projectId, idempotencyKey, ct);
        return Ok(session);
    }

    [HttpPatch("agent/session/{sessionId}")]
    public async Task<IActionResult> UpdateSession(string sessionId, [FromBody] AgentSessionUpdateRequest request, CancellationToken ct)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            var updateRequest = new Models.AgentSessions.UpdateAgentSessionRequest
            {
                Title = request.Title,
                IsArchived = request.IsArchived
            };

            var session = await _agentSessionService.UpdateSessionAsync(sessionId, updateRequest, userId, isAdmin, ct);
            return Ok(session);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Error("SESSION_NOT_FOUND", "会话不存在。", recoverable: false));
        }
    }

    private static object Error(
        string code,
        string message,
        bool recoverable,
        string recommendedAction = "") =>
        new { code, message, recoverable, recommendedAction };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

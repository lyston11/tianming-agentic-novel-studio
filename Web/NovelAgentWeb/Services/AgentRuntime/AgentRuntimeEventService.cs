using System.Text.Json;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public sealed class AgentRuntimeEventService : IAgentRuntimeEventService
{
    private readonly NovelAgentDbContext _db;
    private readonly AgentSseEventBus? _events;
    private readonly IAgentRuntimeEventFanout? _fanout;

    public AgentRuntimeEventService(
        NovelAgentDbContext db,
        AgentSseEventBus? events = null,
        IAgentRuntimeEventFanout? fanout = null)
    {
        _db = db;
        _events = events;
        _fanout = fanout;
    }

    public async Task<AgentRuntimeEvent> AppendAsync(CreateAgentRuntimeEventRequest request, CancellationToken ct = default)
    {
        var sourceMessageId = await ResolveSourceMessageIdAsync(request, ct).ConfigureAwait(false);
        var data = MergeRuntimeEventData(request.Data, request.RuntimeRunId, sourceMessageId);
        var dataJson = Serialize(data);
        var normalizedProjectId = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId;
        var normalizedStage = Normalize(request.Stage);
        var normalizedStatus = Normalize(request.Status);
        var normalizedArtifactType = Normalize(request.ArtifactType);
        var normalizedArtifactId = Normalize(request.ArtifactId);
        var displaySurface = NormalizeOrDefault(request.DisplaySurface, AgentRuntimeEventSurface.Chat);
        var displayPolicy = NormalizeOrDefault(request.DisplayPolicy, AgentRuntimeEventDisplayPolicy.Collapsible);
        var existing = await _db.AgentRuntimeEvents
            .FirstOrDefaultAsync(evt =>
                evt.RuntimeRunId == request.RuntimeRunId &&
                evt.UserId == request.UserId &&
                evt.SessionId == request.SessionId &&
                evt.ProjectId == normalizedProjectId &&
                evt.Type == request.Type &&
                evt.Stage == normalizedStage &&
                evt.Status == normalizedStatus &&
                evt.ArtifactType == normalizedArtifactType &&
                evt.ArtifactId == normalizedArtifactId &&
                evt.DisplaySurface == displaySurface &&
                evt.DisplayPolicy == displayPolicy &&
                evt.Message == request.Message &&
                evt.DataJson == dataJson,
                ct)
            .ConfigureAwait(false);
        if (existing != null)
            return existing;

        var evt = new AgentRuntimeEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            RuntimeRunId = request.RuntimeRunId,
            UserId = request.UserId,
            SessionId = request.SessionId,
            ProjectId = normalizedProjectId,
            Type = request.Type,
            Stage = normalizedStage,
            Status = normalizedStatus,
            ArtifactType = normalizedArtifactType,
            ArtifactId = normalizedArtifactId,
            DisplaySurface = displaySurface,
            DisplayPolicy = displayPolicy,
            Message = request.Message,
            DataJson = dataJson,
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentRuntimeEvents.Add(evt);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (request.PublishToSse)
        {
            var sseEvent = new AgentSseEvent
            {
                EventId = evt.Id,
                Type = evt.Type,
                RunId = evt.RuntimeRunId,
                SourceMessageId = sourceMessageId,
                Stage = evt.Stage,
                Status = evt.Status,
                ArtifactType = evt.ArtifactType,
                ArtifactId = evt.ArtifactId,
                DisplaySurface = evt.DisplaySurface,
                DisplayPolicy = evt.DisplayPolicy,
                Message = evt.Message,
                Data = data
            };

            if (_events != null)
                await _events.SendAsync(evt.UserId, evt.SessionId, sseEvent, ct).ConfigureAwait(false);

            if (_fanout != null)
                await _fanout.PublishAsync(evt.UserId, evt.SessionId, sseEvent, ct).ConfigureAwait(false);
        }
        return evt;
    }

    public async Task<IReadOnlyList<AgentRuntimeEvent>> GetRecentAsync(
        string userId,
        string sessionId,
        int limit = 50,
        CancellationToken ct = default,
        string? afterEventId = null)
    {
        var query = _db.AgentRuntimeEvents
            .Where(e => e.UserId == userId && e.SessionId == sessionId)
            .AsQueryable();

        query = await ApplyCursorAsync(
                query,
                e => e.UserId == userId && e.SessionId == sessionId,
                afterEventId,
                ct)
            .ConfigureAwait(false);

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentRuntimeEvent>> GetForRunAsync(
        string runtimeRunId,
        int limit = 100,
        CancellationToken ct = default,
        string? afterEventId = null)
    {
        var query = _db.AgentRuntimeEvents
            .Where(e => e.RuntimeRunId == runtimeRunId)
            .AsQueryable();

        query = await ApplyCursorAsync(
                query,
                e => e.RuntimeRunId == runtimeRunId,
                afterEventId,
                ct)
            .ConfigureAwait(false);

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private static string Serialize(object? value) =>
        value == null
            ? "{}"
            : JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private async Task<string> ResolveSourceMessageIdAsync(CreateAgentRuntimeEventRequest request, CancellationToken ct)
    {
        var fromData = ExtractStringProperty(request.Data, "sourceMessageId");
        if (!string.IsNullOrWhiteSpace(fromData))
            return fromData.Trim();

        if (string.IsNullOrWhiteSpace(request.RuntimeRunId))
            return string.Empty;

        return await _db.AgentRuntimeRuns
            .Where(run => run.Id == request.RuntimeRunId)
            .Select(run => run.SourceMessageId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? string.Empty;
    }

    private static object? MergeRuntimeEventData(object? data, string runtimeRunId, string sourceMessageId)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(runtimeRunId))
            result["runtimeRunId"] = runtimeRunId.Trim();
        if (!string.IsNullOrWhiteSpace(sourceMessageId))
            result["sourceMessageId"] = sourceMessageId.Trim();

        if (data == null)
            return result.Count == 0 ? null : result;

        try
        {
            var element = JsonSerializer.SerializeToElement(data);
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                    result[property.Name] = property.Value.Clone();
            }
            else
            {
                result["payload"] = element.Clone();
            }
        }
        catch
        {
            result["payload"] = data;
        }

        if (!string.IsNullOrWhiteSpace(runtimeRunId))
            result["runtimeRunId"] = runtimeRunId.Trim();
        if (!string.IsNullOrWhiteSpace(sourceMessageId))
            result["sourceMessageId"] = sourceMessageId.Trim();

        return result;
    }

    private static string ExtractStringProperty(object? data, string propertyName)
    {
        if (data == null)
            return string.Empty;

        try
        {
            var element = JsonSerializer.SerializeToElement(data);
            if (element.ValueKind != JsonValueKind.Object)
                return string.Empty;
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeOrDefault(string? value, string fallback)
    {
        var normalized = Normalize(value);
        return normalized == string.Empty ? fallback : normalized;
    }

    private async Task<IQueryable<AgentRuntimeEvent>> ApplyCursorAsync(
        IQueryable<AgentRuntimeEvent> query,
        Expression<Func<AgentRuntimeEvent, bool>> cursorScope,
        string? afterEventId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(afterEventId))
            return query;

        var cursorCreatedAt = await _db.AgentRuntimeEvents
            .Where(cursorScope)
            .Where(e => e.Id == afterEventId.Trim())
            .Select(e => (DateTime?)e.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return cursorCreatedAt.HasValue
            ? query.Where(e => e.CreatedAt > cursorCreatedAt.Value)
            : query;
    }
}

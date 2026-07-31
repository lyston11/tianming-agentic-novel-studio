using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Models.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentSessions;

public sealed class AgentSessionResumeService : IAgentSessionResumeService
{
    private readonly AgentSessionManager _sessions;
    private readonly NovelAgentDbContext _db;
    private readonly IAgentRuntimeEventService _runtimeEvents;

    public AgentSessionResumeService(
        AgentSessionManager sessions,
        NovelAgentDbContext db,
        IAgentRuntimeEventService runtimeEvents)
    {
        _sessions = sessions;
        _db = db;
        _runtimeEvents = runtimeEvents;
    }

    public async Task<AgentSessionResumeResponse> ResumeAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _sessions.GetSessionAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null)
            throw new KeyNotFoundException($"Session {sessionId} not found");

        var recentToolExecutions = await _db.AgentToolExecutions.AsNoTracking()
            .Where(item => item.UserId == session.UserId && item.SessionId == session.SessionId)
            .OrderByDescending(item => item.StartedAt)
            .Take(20)
            .Select(item => new AgentToolExecutionSnapshot
            {
                Id = item.Id,
                ToolName = item.ToolName,
                Status = item.Status,
                RunId = item.RunId,
                Phase = item.Phase,
                ResultPhase = item.ResultPhase,
                ResultMessage = item.ResultMessage,
                RecommendedNextTool = item.RecommendedNextTool,
                StartedAt = item.StartedAt,
                CompletedAt = item.CompletedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var recentRuntimeEvents = await _runtimeEvents
            .GetRecentAsync(session.UserId, session.SessionId, 12, ct)
            .ConfigureAwait(false);

        return new AgentSessionResumeResponse
        {
            SessionId = session.SessionId,
            Title = session.Title,
            Phase = session.Phase,
            ActiveProjectId = session.ActiveProjectId,
            ActiveRunId = null,
            IsArchived = session.IsArchived,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            Messages = session.ChatHistory.ToList(),
            Memory = AgentWorkingMemorySnapshot.From(session.WorkingMemory),
            MissionPlan = session.WorkingMemory.MissionPlan,
            PendingToolCall = null,
            PendingConfirmation = null,
            HasPendingTool = false,
            HasPendingConfirmation = false,
            DiscoveredPhase = session.DiscoveredPhase,
            DiscoveredTools = Array.Empty<ToolSchema>(),
            ToolSearchCacheVersion = null,
            LastToolSearchAt = null,
            ToolSearchCacheFresh = false,
            ToolSearchCacheSource = "legacy_retired",
            RecentToolExecutions = recentToolExecutions,
            RecentRuntimeEvents = recentRuntimeEvents.Select(ToRuntimeEventView).ToList(),
            RunHistory = session.RunHistory.ToList()
        };
    }

    private static AgentRuntimeEventView ToRuntimeEventView(TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent evt)
    {
        JsonElement data;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(evt.DataJson) ? "{}" : evt.DataJson);
            data = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse("{}");
            data = doc.RootElement.Clone();
        }

        return new AgentRuntimeEventView
        {
            EventId = evt.Id,
            Type = evt.Type,
            RunId = evt.RuntimeRunId,
            SourceMessageId = ExtractString(data, "sourceMessageId"),
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

    private static string ExtractString(JsonElement data, string propertyName)
    {
        if (data.ValueKind != JsonValueKind.Object)
            return string.Empty;

        foreach (var property in data.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

using System.Text.Json;
using TM.Web.NovelAgentWeb.Models.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentSessions;

public sealed class AgentSessionResumeService : IAgentSessionResumeService
{
    private readonly AgentSessionManager _sessions;
    private readonly IToolSearchCacheService _toolSearchCache;
    private readonly IAgentToolExecutionLedger _toolExecutionLedger;
    private readonly IAgentRuntimeRunService _runtimeRuns;
    private readonly IAgentRuntimeEventService _runtimeEvents;
    private readonly AgentToolRegistry _toolRegistry;

    public AgentSessionResumeService(
        AgentSessionManager sessions,
        IToolSearchCacheService toolSearchCache,
        IAgentToolExecutionLedger toolExecutionLedger,
        IAgentRuntimeRunService runtimeRuns,
        IAgentRuntimeEventService runtimeEvents,
        AgentToolRegistry toolRegistry)
    {
        _sessions = sessions;
        _toolSearchCache = toolSearchCache;
        _toolExecutionLedger = toolExecutionLedger;
        _runtimeRuns = runtimeRuns;
        _runtimeEvents = runtimeEvents;
        _toolRegistry = toolRegistry;
    }

    public async Task<AgentSessionResumeResponse> ResumeAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _sessions.GetSessionAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null)
            throw new KeyNotFoundException($"Session {sessionId} not found");

        var phase = string.IsNullOrWhiteSpace(session.DiscoveredPhase)
            ? session.Phase
            : session.DiscoveredPhase;
        if (string.IsNullOrWhiteSpace(phase))
            phase = "Conversation";

        var toolCatalogSignature = ToolCatalogSignature.Compute(_toolRegistry.ListToolSchemas());
        var toolSearchLookup = await _toolSearchCache.GetAsync(session, phase, toolCatalogSignature, ct).ConfigureAwait(false);
        var tools = toolSearchLookup.Tools?.ToList() ?? new List<ToolSchema>();
        var recentToolExecutions = await _toolExecutionLedger
            .GetRecentAsync(
                session.UserId,
                session.SessionId,
                string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
                ct)
            .ConfigureAwait(false);
        var activeRuntimeRun = await _runtimeRuns.TryGetActiveAsync(session.UserId, session.SessionId, ct).ConfigureAwait(false);
        var recentRuntimeEvents = await _runtimeEvents
            .GetRecentAsync(session.UserId, session.SessionId, 12, ct)
            .ConfigureAwait(false);

        return new AgentSessionResumeResponse
        {
            SessionId = session.SessionId,
            Title = session.Title,
            Phase = session.Phase,
            ActiveProjectId = session.ActiveProjectId,
            ActiveRunId = activeRuntimeRun?.Id,
            IsArchived = session.IsArchived,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            Messages = session.ChatHistory.ToList(),
            Memory = AgentWorkingMemorySnapshot.From(session.WorkingMemory),
            MissionPlan = session.WorkingMemory.MissionPlan,
            PendingToolCall = session.WorkingMemory.PendingToolCall,
            PendingConfirmation = session.WorkingMemory.PendingConfirmation,
            HasPendingTool = session.WorkingMemory.PendingToolCall != null,
            HasPendingConfirmation = session.WorkingMemory.PendingConfirmation != null,
            DiscoveredPhase = session.DiscoveredPhase,
            DiscoveredTools = tools,
            ToolSearchCacheVersion = session.ToolSearchCacheVersion,
            LastToolSearchAt = session.LastToolSearchAt,
            ToolSearchCacheFresh = toolSearchLookup.Hit,
            ToolSearchCacheSource = toolSearchLookup.Source,
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

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

    public AgentSessionResumeService(
        AgentSessionManager sessions,
        IToolSearchCacheService toolSearchCache,
        IAgentToolExecutionLedger toolExecutionLedger,
        IAgentRuntimeRunService runtimeRuns,
        IAgentRuntimeEventService runtimeEvents)
    {
        _sessions = sessions;
        _toolSearchCache = toolSearchCache;
        _toolExecutionLedger = toolExecutionLedger;
        _runtimeRuns = runtimeRuns;
        _runtimeEvents = runtimeEvents;
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

        var toolSearchLookup = await _toolSearchCache.GetAsync(session, phase, ct).ConfigureAwait(false);
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
            Type = evt.Type,
            RunId = evt.RuntimeRunId,
            Message = evt.Message,
            Data = data,
            Timestamp = evt.CreatedAt
        };
    }
}

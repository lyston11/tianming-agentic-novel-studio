using TM.Web.NovelAgentWeb.Models.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentSessions;

public sealed class AgentSessionResumeService : IAgentSessionResumeService
{
    private readonly AgentSessionManager _sessions;
    private readonly IToolSearchCacheService _toolSearchCache;
    private readonly IAgentToolExecutionLedger _toolExecutionLedger;

    public AgentSessionResumeService(
        AgentSessionManager sessions,
        IToolSearchCacheService toolSearchCache,
        IAgentToolExecutionLedger toolExecutionLedger)
    {
        _sessions = sessions;
        _toolSearchCache = toolSearchCache;
        _toolExecutionLedger = toolExecutionLedger;
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

        return new AgentSessionResumeResponse
        {
            SessionId = session.SessionId,
            Title = session.Title,
            Phase = session.Phase,
            ActiveProjectId = session.ActiveProjectId,
            ActiveRunId = session.ActiveRunId,
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
            RunHistory = session.RunHistory.ToList()
        };
    }
}

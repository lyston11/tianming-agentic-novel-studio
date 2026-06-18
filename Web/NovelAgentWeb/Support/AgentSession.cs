using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = "新会话";
    public string Phase { get; set; } = "idle";
    public string ActiveProjectId { get; set; } = string.Empty;
    public string? ActiveRunId { get; set; }
    public bool IsArchived { get; set; }
    public List<string> RunHistory { get; set; } = new();
    public List<AgentConversationTurn> ChatHistory { get; set; } = new();
    public AgentWorkingMemory WorkingMemory { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Tool Search缓存
    public string? DiscoveredPhase { get; set; }
    public List<ToolSchema> DiscoveredTools { get; set; } = new();
    public DateTime? LastToolSearchAt { get; set; }
    public string? ToolSearchCacheVersion { get; set; }

}

public sealed class AgentConversationTurn
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentSessionManager
{
    private readonly TM.Web.NovelAgentWeb.Data.NovelAgentDbContext _db;
    private readonly TM.Web.NovelAgentWeb.Services.Auth.ICurrentUserService _currentUser;
    private readonly IChatHistoryRepository? _chatHistory;
    private readonly AgentSseEventBus _events;

    public AgentSessionManager(
        TM.Web.NovelAgentWeb.Data.NovelAgentDbContext db,
        TM.Web.NovelAgentWeb.Services.Auth.ICurrentUserService currentUser,
        IChatHistoryRepository? chatHistory = null,
        AgentSseEventBus? events = null)
    {
        _db = db;
        _currentUser = currentUser;
        _chatHistory = chatHistory;
        _events = events ?? new AgentSseEventBus();
    }

    public async Task<AgentSession> GetOrCreateSessionAsync(string? sessionId = null, CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserId();

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            // New session: no projectId, LLM will decide later
            return new AgentSession { UserId = userId, ActiveProjectId = string.Empty };
        }

        var entity = await _db.AgentSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        if (entity == null)
        {
            return new AgentSession { SessionId = sessionId, UserId = userId, ActiveProjectId = string.Empty };
        }

        var result = DeserializeSession(entity);
        await HydrateChatHistoryAsync(result, ct).ConfigureAwait(false);

        // Clean stale projectId: if project no longer exists, clear it
        if (!string.IsNullOrWhiteSpace(result.ActiveProjectId))
        {
            var projectExists = await _db.NovelProjects
                .AnyAsync(p => p.Id == result.ActiveProjectId && p.UserId == userId, ct);
            if (!projectExists)
                result.ActiveProjectId = string.Empty;
        }

        return result;
    }

    public async Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserId();
        var entity = await _db.AgentSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (entity == null)
            return null;

        var session = DeserializeSession(entity);
        await HydrateChatHistoryAsync(session, ct).ConfigureAwait(false);
        return session;
    }

    public async Task<IReadOnlyList<AgentSession>> ListSessionsAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserId();
        var entities = await _db.AgentSessions
            .Where(s => s.UserId == userId && !s.IsArchived)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);
        var sessions = entities.Select(DeserializeSession).ToList();
        foreach (var session in sessions)
            await HydrateChatHistoryAsync(session, ct).ConfigureAwait(false);
        return sessions;
    }

    public async Task SaveSessionAsync(AgentSession session, CancellationToken ct = default)
    {
        var entity = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == session.SessionId, ct);

        if (entity == null)
        {
            entity = new TM.Web.NovelAgentWeb.Data.Entities.AgentSession
            {
                Id = session.SessionId,
                UserId = session.UserId,
                Title = session.Title,
                ProjectId = string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
                IsArchived = session.IsArchived,
                SessionData = SerializeSessionData(session),
                CreatedAt = session.CreatedAt,
                UpdatedAt = DateTime.UtcNow
            };
            _db.AgentSessions.Add(entity);
        }
        else
        {
            if (entity.UserId != session.UserId)
                throw new UnauthorizedAccessException($"Session {session.SessionId} belongs to another user");

            entity.Title = session.Title;
            entity.ProjectId = string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId;
            entity.IsArchived = session.IsArchived;
            entity.SessionData = SerializeSessionData(session);
            entity.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task SendEventAsync(string sessionId, AgentSseEvent evt, CancellationToken ct = default)
        => await _events.SendAsync(sessionId, evt, ct).ConfigureAwait(false);

    public ChannelReader<AgentSseEvent> GetEventReader(string sessionId) =>
        _events.GetReader(sessionId);

    public async Task RemoveSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserId();
        var entity = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (entity != null)
        {
            _db.AgentSessions.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
        _events.RemoveSession(sessionId);
    }

    private static string SerializeSessionData(AgentSession session) =>
        JsonSerializer.Serialize(new
        {
            phase = session.Phase,
            activeRunId = session.ActiveRunId,
            runHistory = session.RunHistory,
            runtimeState = RuntimeSessionStateData.From(session.WorkingMemory),
            toolSearchCache = new
            {
                discoveredPhase = session.DiscoveredPhase,
                lastToolSearchAt = session.LastToolSearchAt,
                version = session.ToolSearchCacheVersion
            }
        }, JsonOptions());

    private static AgentSession DeserializeSession(TM.Web.NovelAgentWeb.Data.Entities.AgentSession entity)
    {
        var data = string.IsNullOrWhiteSpace(entity.SessionData)
            ? null
            : JsonSerializer.Deserialize<SessionData>(entity.SessionData, JsonOptions());

        return new AgentSession
        {
            SessionId = entity.Id,
            UserId = entity.UserId,
            Title = entity.Title,
            Phase = data?.Phase ?? "idle",
            ActiveProjectId = entity.ProjectId ?? string.Empty,
            ActiveRunId = data?.ActiveRunId,
            IsArchived = entity.IsArchived,
            RunHistory = data?.RunHistory ?? new(),
            WorkingMemory = data?.RuntimeState?.ToWorkingMemory() ?? new(),
            DiscoveredPhase = data?.ToolSearchCache?.DiscoveredPhase,
            LastToolSearchAt = data?.ToolSearchCache?.LastToolSearchAt,
            ToolSearchCacheVersion = data?.ToolSearchCache?.Version,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private async Task HydrateChatHistoryAsync(AgentSession session, CancellationToken ct)
    {
        if (_chatHistory == null)
            return;

        var turns = await _chatHistory.GetHotWindowAsync(
                session.UserId,
                string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
                session.SessionId,
                ct)
            .ConfigureAwait(false);

        session.ChatHistory = turns
            .Select(turn => new AgentConversationTurn
            {
                Role = turn.Role,
                Content = turn.Content,
                CreatedAt = turn.CreatedAt
            })
            .ToList();
    }

    private class SessionData
    {
        public string Phase { get; set; } = "idle";
        public string? ActiveRunId { get; set; }
        public List<string> RunHistory { get; set; } = new();
        public RuntimeSessionStateData? RuntimeState { get; set; }
        public ToolSearchCache? ToolSearchCache { get; set; }
    }

    private sealed class RuntimeSessionStateData
    {
        public AgentToolCall? PendingToolCall { get; set; }
        public AgentPendingConfirmation? PendingConfirmation { get; set; }
        public AgentDecision? LastDecision { get; set; }
        public MissionPointerData MissionPointer { get; set; } = new();

        public static RuntimeSessionStateData From(AgentWorkingMemory memory) => new()
        {
            PendingToolCall = memory.PendingToolCall,
            PendingConfirmation = memory.PendingConfirmation,
            LastDecision = memory.LastDecision,
            MissionPointer = MissionPointerData.From(memory.MissionPlan)
        };

        public AgentWorkingMemory ToWorkingMemory()
        {
            var memory = new AgentWorkingMemory
            {
                PendingToolCall = PendingToolCall,
                PendingConfirmation = PendingConfirmation,
                LastDecision = LastDecision,
            };
            MissionPointer.ApplyTo(memory.MissionPlan);
            return memory;
        }
    }

    private sealed class MissionPointerData
    {
        public string MissionId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CurrentRunId { get; set; } = string.Empty;
        public string Stage { get; set; } = "idle";
        public string Status { get; set; } = "idle";
        public string ActiveTaskId { get; set; } = string.Empty;
        public string ActiveChapterId { get; set; } = string.Empty;
        public string ActiveTurnId { get; set; } = string.Empty;
        public string ActiveToolTransactionId { get; set; } = string.Empty;
        public string ArtifactCursor { get; set; } = string.Empty;

        public static MissionPointerData From(AgentMissionPlan? plan)
        {
            if (plan == null)
                return new MissionPointerData();

            return new MissionPointerData
            {
                MissionId = plan.MissionId,
                ProjectId = plan.ProjectId,
                CurrentRunId = plan.CurrentRunId,
                Stage = plan.Stage,
                Status = plan.Status,
                ActiveTaskId = plan.SchedulerState?.ActiveTaskId ?? string.Empty,
                ActiveChapterId = FirstNonEmpty(plan.ActiveChapterId, plan.SchedulerState?.ActiveChapterId),
                ActiveTurnId = plan.ActiveTurnId,
                ActiveToolTransactionId = plan.ActiveToolTransactionId,
                ArtifactCursor = FirstNonEmpty(plan.ActiveArtifactCursor, plan.ArtifactCursor)
            };
        }

        public void ApplyTo(AgentMissionPlan plan)
        {
            if (!string.IsNullOrWhiteSpace(MissionId))
                plan.MissionId = MissionId;
            plan.ProjectId = ProjectId ?? string.Empty;
            plan.CurrentRunId = CurrentRunId ?? string.Empty;
            plan.Stage = string.IsNullOrWhiteSpace(Stage) ? "idle" : Stage;
            plan.Status = string.IsNullOrWhiteSpace(Status) ? "idle" : Status;
            plan.ActiveChapterId = ActiveChapterId ?? string.Empty;
            plan.ActiveTurnId = ActiveTurnId ?? string.Empty;
            plan.ActiveToolTransactionId = ActiveToolTransactionId ?? string.Empty;
            plan.ArtifactCursor = ArtifactCursor ?? string.Empty;
            plan.ActiveArtifactCursor = ArtifactCursor ?? string.Empty;
            plan.SchedulerState.ActiveTaskId = ActiveTaskId ?? string.Empty;
            plan.SchedulerState.ActiveChapterId = ActiveChapterId ?? string.Empty;
            plan.SchedulerState.ActiveRunId = CurrentRunId ?? string.Empty;
        }
    }

    private class ToolSearchCache
    {
        public string? DiscoveredPhase { get; set; }
        public DateTime? LastToolSearchAt { get; set; }
        public string? Version { get; set; }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

public sealed class AgentSseEventBus
{
    private readonly ConcurrentDictionary<string, Channel<AgentSseEvent>> _channels = new();

    public ChannelReader<AgentSseEvent> GetReader(string sessionId) =>
        GetOrCreateChannel(sessionId).Reader;

    public async Task SendAsync(string sessionId, AgentSseEvent evt, CancellationToken ct = default)
    {
        evt.SessionId = sessionId;
        evt.Timestamp = DateTime.UtcNow;
        var channel = GetOrCreateChannel(sessionId);
        await channel.Writer.WriteAsync(evt, ct).ConfigureAwait(false);
    }

    public void RemoveSession(string sessionId)
    {
        if (_channels.TryRemove(sessionId, out var channel))
            channel.Writer.TryComplete();
    }

    private Channel<AgentSseEvent> GetOrCreateChannel(string sessionId) =>
        _channels.GetOrAdd(sessionId, _ => Channel.CreateUnbounded<AgentSseEvent>());
}

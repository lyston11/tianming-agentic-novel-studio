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
    public string RuntimeRunId { get; set; } = string.Empty;
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
    public string TurnId { get; set; } = string.Empty;
    public int TurnIndex { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public AgentKnowledgeContext? Knowledge { get; set; }
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

    public async Task SendEventAsync(
        string userId,
        string sessionId,
        AgentSseEvent evt,
        CancellationToken ct = default) =>
        await _events.SendAsync(userId, sessionId, evt, ct).ConfigureAwait(false);

    public async Task<bool> ReplaceLastAssistantTurnAsync(
        AgentSession session,
        string expectedContent,
        string replacementContent,
        CancellationToken ct = default)
    {
        var expected = expectedContent.Trim();
        var replacement = replacementContent.Trim();
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(replacement))
            return false;

        var replaced = true;
        if (_chatHistory != null)
        {
            var replaceTask = _chatHistory.ReplaceLastAssistantTurnAsync(
                session.UserId,
                string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
                session.SessionId,
                expected,
                replacement,
                ct);
            replaced = replaceTask != null && await replaceTask.ConfigureAwait(false);
        }
        if (!replaced)
            return false;

        var lastAssistantTurn = session.ChatHistory.LastOrDefault(t =>
            string.Equals(t.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Content, expected, StringComparison.Ordinal));
        if (lastAssistantTurn != null)
            lastAssistantTurn.Content = replacement;

        return true;
    }

    public AgentSseSubscription SubscribeEvents(
        string userId,
        string sessionId,
        bool includeBacklog = true) =>
        _events.Subscribe(userId, sessionId, includeBacklog);

    public async Task RemoveSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserId();
        var entity = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (entity != null)
        {
            _db.AgentSessions.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
        _events.RemoveSession(userId, sessionId);
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
                TurnId = turn.TurnId,
                TurnIndex = turn.TurnIndex,
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
        public List<AgentRuntimeInterruptObservation> RuntimeInterrupts { get; set; } = new();
        public MissionPointerData MissionPointer { get; set; } = new();

        public static RuntimeSessionStateData From(AgentWorkingMemory memory) => new()
        {
            PendingToolCall = memory.PendingToolCall,
            PendingConfirmation = memory.PendingConfirmation,
            LastDecision = memory.LastDecision,
            RuntimeInterrupts = memory.RuntimeInterrupts.TakeLast(16).ToList(),
            MissionPointer = MissionPointerData.From(memory.MissionPlan)
        };

        public AgentWorkingMemory ToWorkingMemory()
        {
            var memory = new AgentWorkingMemory
            {
                PendingToolCall = PendingToolCall,
                PendingConfirmation = PendingConfirmation,
                LastDecision = LastDecision,
                RuntimeInterrupts = RuntimeInterrupts.TakeLast(16).ToList(),
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
    private const int MaxBacklogEvents = 256;
    private readonly ConcurrentDictionary<EventStreamScope, SessionEventStream> _streams = new();

    public AgentSseSubscription Subscribe(string userId, string sessionId, bool includeBacklog = true)
    {
        var scope = EventStreamScope.Create(userId, sessionId);
        var stream = _streams.GetOrAdd(scope, _ => new SessionEventStream());
        var channel = Channel.CreateUnbounded<AgentSseEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        long subscriberId;

        lock (stream.Sync)
        {
            subscriberId = ++stream.NextSubscriberId;
            stream.Subscribers[subscriberId] = channel;
            if (includeBacklog)
            {
                while (stream.Backlog.TryDequeue(out var evt))
                    channel.Writer.TryWrite(evt);
            }
            else
            {
                stream.Backlog.Clear();
            }
        }

        return new AgentSseSubscription(
            channel.Reader,
            () =>
            {
                RemoveSubscriber(scope, stream, subscriberId, channel);
                return ValueTask.CompletedTask;
            });
    }

    public Task SendAsync(
        string userId,
        string sessionId,
        AgentSseEvent evt,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var scope = EventStreamScope.Create(userId, sessionId);
        if (string.IsNullOrWhiteSpace(evt.EventId))
            evt.EventId = Guid.NewGuid().ToString("N");
        evt.SessionId = sessionId;
        evt.Timestamp = DateTime.UtcNow;
        var stream = _streams.GetOrAdd(scope, _ => new SessionEventStream());

        lock (stream.Sync)
        {
            if (stream.Subscribers.Count == 0)
            {
                while (stream.Backlog.Count >= MaxBacklogEvents)
                    stream.Backlog.Dequeue();
                stream.Backlog.Enqueue(evt);
            }
            else
            {
                foreach (var channel in stream.Subscribers.Values)
                    channel.Writer.TryWrite(evt);
            }
        }

        return Task.CompletedTask;
    }

    public void RemoveSession(string userId, string sessionId)
    {
        var scope = EventStreamScope.Create(userId, sessionId);
        if (!_streams.TryRemove(scope, out var stream))
            return;

        lock (stream.Sync)
        {
            foreach (var channel in stream.Subscribers.Values)
                channel.Writer.TryComplete();
            stream.Subscribers.Clear();
            stream.Backlog.Clear();
        }
    }

    private void RemoveSubscriber(
        EventStreamScope scope,
        SessionEventStream stream,
        long subscriberId,
        Channel<AgentSseEvent> channel)
    {
        var removeStream = false;
        lock (stream.Sync)
        {
            stream.Subscribers.Remove(subscriberId);
            channel.Writer.TryComplete();
            removeStream = stream.Subscribers.Count == 0 && stream.Backlog.Count == 0;
        }

        if (removeStream)
            ((ICollection<KeyValuePair<EventStreamScope, SessionEventStream>>)_streams)
                .Remove(new KeyValuePair<EventStreamScope, SessionEventStream>(scope, stream));
    }

    private readonly record struct EventStreamScope(string UserId, string SessionId)
    {
        public static EventStreamScope Create(string userId, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("userId is required for SSE event scope.", nameof(userId));
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("sessionId is required for SSE event scope.", nameof(sessionId));
            return new EventStreamScope(userId.Trim(), sessionId.Trim());
        }
    }

    private sealed class SessionEventStream
    {
        public object Sync { get; } = new();
        public Dictionary<long, Channel<AgentSseEvent>> Subscribers { get; } = new();
        public Queue<AgentSseEvent> Backlog { get; } = new();
        public long NextSubscriberId { get; set; }
    }
}

public sealed class AgentSseSubscription : IAsyncDisposable
{
    private Func<ValueTask>? _dispose;

    internal AgentSseSubscription(ChannelReader<AgentSseEvent> reader, Func<ValueTask> dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    public ChannelReader<AgentSseEvent> Reader { get; }

    public async ValueTask DisposeAsync()
    {
        var dispose = Interlocked.Exchange(ref _dispose, null);
        if (dispose != null)
            await dispose().ConfigureAwait(false);
    }
}

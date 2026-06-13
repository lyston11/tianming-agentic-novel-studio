using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;

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

    public void NormalizeLegacyState()
    {
        if (IsRemovedLegacyTool(WorkingMemory.PendingToolCall))
            WorkingMemory.PendingToolCall = null;
        if (IsRemovedLegacyTool(WorkingMemory.PendingConfirmation?.ToolCall))
            WorkingMemory.PendingConfirmation = null;
        WorkingMemory.PendingToolCall = null;
        WorkingMemory.PendingConfirmation = null;
        if (string.Equals(Phase, "awaiting_confirmation", StringComparison.OrdinalIgnoreCase))
            Phase = "idle";
        if (string.Equals(WorkingMemory.MissionPlan.Status, "awaiting_confirmation", StringComparison.OrdinalIgnoreCase))
            WorkingMemory.MissionPlan.Status = string.Empty;
        if (string.Equals(WorkingMemory.MissionPlan.Stage, "awaiting_confirmation", StringComparison.OrdinalIgnoreCase))
            WorkingMemory.MissionPlan.Stage = string.Empty;
    }

    private static bool IsRemovedLegacyTool(AgentToolCall? call) =>
        call != null && string.Equals(call.Name, "GenerateChapter", StringComparison.OrdinalIgnoreCase);
}

public sealed class AgentConversationTurn
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentSessionManager
{
    private readonly ConcurrentDictionary<string, Channel<AgentSseEvent>> _channels = new();
    private readonly TM.Web.NovelAgentWeb.Data.NovelAgentDbContext _db;
    private readonly TM.Web.NovelAgentWeb.Services.Auth.ICurrentUserService _currentUser;

    public AgentSessionManager(
        TM.Web.NovelAgentWeb.Data.NovelAgentDbContext db,
        TM.Web.NovelAgentWeb.Services.Auth.ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<AgentSession> GetOrCreateSessionAsync(string? sessionId = null, CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserId();

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            // New session: no projectId, LLM will decide later
            var session = new AgentSession { UserId = userId, ActiveProjectId = string.Empty };
            NormalizeLegacyPending(session);
            return session;
        }

        var entity = await _db.AgentSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        if (entity == null)
        {
            var session = new AgentSession { SessionId = sessionId, UserId = userId, ActiveProjectId = string.Empty };
            NormalizeLegacyPending(session);
            return session;
        }

        var result = DeserializeSession(entity);

        // Clean stale projectId: if project no longer exists, clear it
        if (!string.IsNullOrWhiteSpace(result.ActiveProjectId))
        {
            var projectExists = await _db.NovelProjects
                .AnyAsync(p => p.Id == result.ActiveProjectId && p.UserId == userId, ct);
            if (!projectExists)
                result.ActiveProjectId = string.Empty;
        }

        NormalizeLegacyPending(result);
        return result;
    }

    public async Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserId();
        var entity = await _db.AgentSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        return entity == null ? null : DeserializeSession(entity);
    }

    public async Task<IReadOnlyList<AgentSession>> ListSessionsAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserId();
        var entities = await _db.AgentSessions
            .Where(s => s.UserId == userId && !s.IsArchived)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);
        return entities.Select(DeserializeSession).ToList();
    }

    public async Task SaveSessionAsync(AgentSession session, CancellationToken ct = default)
    {
        NormalizeLegacyPending(session);

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

    public Channel<AgentSseEvent> GetOrCreateChannel(string sessionId) =>
        _channels.GetOrAdd(sessionId, _ => Channel.CreateUnbounded<AgentSseEvent>());

    public async Task SendEventAsync(string sessionId, AgentSseEvent evt, CancellationToken ct = default)
    {
        evt.SessionId = sessionId;
        evt.Timestamp = DateTime.UtcNow;
        var channel = GetOrCreateChannel(sessionId);
        await channel.Writer.WriteAsync(evt, ct);
    }

    public ChannelReader<AgentSseEvent> GetEventReader(string sessionId) =>
        GetOrCreateChannel(sessionId).Reader;

    public async Task RemoveSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserId();
        var entity = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (entity != null)
        {
            _db.AgentSessions.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
        if (_channels.TryRemove(sessionId, out var channel))
            channel.Writer.TryComplete();
    }

    private static string SerializeSessionData(AgentSession session) =>
        JsonSerializer.Serialize(new
        {
            phase = session.Phase,
            activeRunId = session.ActiveRunId,
            runHistory = session.RunHistory,
            chatHistory = session.ChatHistory,
            workingMemory = session.WorkingMemory,
            toolSearchCache = new
            {
                discoveredPhase = session.DiscoveredPhase,
                discoveredTools = session.DiscoveredTools,
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
            ChatHistory = data?.ChatHistory ?? new(),
            WorkingMemory = data?.WorkingMemory ?? new(),
            DiscoveredPhase = data?.ToolSearchCache?.DiscoveredPhase,
            DiscoveredTools = data?.ToolSearchCache?.DiscoveredTools ?? new(),
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

    private static void NormalizeLegacyPending(AgentSession session)
    {
        session.NormalizeLegacyState();
    }

    private class SessionData
    {
        public string Phase { get; set; } = "idle";
        public string? ActiveRunId { get; set; }
        public List<string> RunHistory { get; set; } = new();
        public List<AgentConversationTurn> ChatHistory { get; set; } = new();
        public AgentWorkingMemory WorkingMemory { get; set; } = new();
        public ToolSearchCache? ToolSearchCache { get; set; }
    }

    private class ToolSearchCache
    {
        public string? DiscoveredPhase { get; set; }
        public List<ToolSchema> DiscoveredTools { get; set; } = new();
        public DateTime? LastToolSearchAt { get; set; }
        public string? Version { get; set; }
    }
}

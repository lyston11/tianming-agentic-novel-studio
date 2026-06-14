using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Extensions;
using TM.Web.NovelAgentWeb.Models.AgentSessions;
using TM.Web.NovelAgentWeb.Support;
using AgentSessionEntity = TM.Web.NovelAgentWeb.Data.Entities.AgentSession;

namespace TM.Web.NovelAgentWeb.Services.AgentSessions;

/// <summary>
/// Service for managing agent sessions with database persistence and user isolation.
/// </summary>
public class AgentSessionService : IAgentSessionService
{
    private readonly NovelAgentDbContext _dbContext;
    private readonly ILogger<AgentSessionService> _logger;

    public AgentSessionService(
        NovelAgentDbContext dbContext,
        ILogger<AgentSessionService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AgentSessionResponse> GetOrCreateSessionAsync(
        string? sessionId,
        string userId,
        string? projectId,
        CancellationToken cancellationToken = default)
    {
        // If sessionId provided, try to get existing session
        if (!string.IsNullOrEmpty(sessionId))
        {
            var existing = await _dbContext.AgentSessions
                .WithUserFilter(userId)
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (existing != null)
            {
                return await MapToResponseAsync(existing, cancellationToken).ConfigureAwait(false);
            }
        }

        // Create new session
        var session = new AgentSessionEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = projectId,
            Title = "新会话",
            SessionData = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.AgentSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created new agent session {SessionId} for user {UserId}", session.Id, userId);

        return await MapToResponseAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentSessionResponse> GetSessionByIdAsync(
        string sessionId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.AgentSessions
            .WithUserFilterIfNotAdmin(userId, isAdmin)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            throw new KeyNotFoundException($"Session {sessionId} not found");
        }

        return await MapToResponseAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<AgentSessionResponse>> ListUserSessionsAsync(
        string userId,
        bool isAdmin,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AgentSessions
            .WithUserFilterIfNotAdmin(userId, isAdmin);

        if (!includeArchived)
        {
            query = query.Where(s => !s.IsArchived);
        }

        var sessions = await query
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);

        var responses = new List<AgentSessionResponse>();
        foreach (var session in sessions)
            responses.Add(await MapToResponseAsync(session, cancellationToken).ConfigureAwait(false));
        return responses;
    }

    public async Task<AgentSessionResponse> UpdateSessionAsync(
        string sessionId,
        UpdateAgentSessionRequest request,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.AgentSessions
            .WithUserFilterIfNotAdmin(userId, isAdmin)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            throw new KeyNotFoundException($"Session {sessionId} not found");
        }

        // Update fields if provided
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            session.Title = request.Title.Trim();
        }

        if (request.IsArchived.HasValue)
        {
            session.IsArchived = request.IsArchived.Value;
        }

        session.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated agent session {SessionId}", sessionId);

        return await MapToResponseAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteSessionAsync(
        string sessionId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.AgentSessions
            .WithUserFilterIfNotAdmin(userId, isAdmin)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            throw new KeyNotFoundException($"Session {sessionId} not found");
        }

        _dbContext.AgentSessions.Remove(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted agent session {SessionId}", sessionId);
    }

    private async Task<AgentSessionResponse> MapToResponseAsync(AgentSessionEntity session, CancellationToken ct)
    {
        var data = DeserializeSessionData(session.SessionData);
        var projectId = session.ProjectId ?? string.Empty;
        var messages = await _dbContext.AgentChatTurns
            .AsNoTracking()
            .Where(t => t.SessionId == session.Id && t.UserId == session.UserId)
            .OrderBy(t => t.TurnIndex)
            .Select(t => new AgentConversationTurn
            {
                Role = t.Role,
                Content = t.Content,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new AgentSessionResponse
        {
            SessionId = session.Id,
            Title = session.Title,
            Phase = string.IsNullOrWhiteSpace(data.Phase) ? "idle" : data.Phase,
            ActiveProjectId = projectId,
            ActiveRunId = data.ActiveRunId,
            IsArchived = session.IsArchived,
            RunHistory = data.RunHistory,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            Messages = messages,
            Memory = new AgentWorkingMemorySnapshot(),
            MessageCount = messages.Count
        };
    }

    private static SessionData DeserializeSessionData(string? sessionData)
    {
        if (string.IsNullOrWhiteSpace(sessionData))
            return new SessionData();

        try
        {
            return JsonSerializer.Deserialize<SessionData>(sessionData, JsonOptions()) ?? new SessionData();
        }
        catch (JsonException)
        {
            return new SessionData();
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class SessionData
    {
        public string Phase { get; set; } = "idle";
        public string? ActiveRunId { get; set; }
        public List<string> RunHistory { get; set; } = new();
    }
}

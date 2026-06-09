using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Extensions;
using TM.Web.NovelAgentWeb.Models.AgentSessions;

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
                return MapToResponse(existing);
            }
        }

        // Create new session
        var session = new AgentSession
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

        return MapToResponse(session);
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

        return MapToResponse(session);
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

        return sessions.Select(MapToResponse).ToList();
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

        return MapToResponse(session);
    }

    public async Task SaveSessionStateAsync(
        string sessionId,
        string sessionData,
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

        session.SessionData = sessionData;
        session.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Saved session state for {SessionId}", sessionId);
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

    private static AgentSessionResponse MapToResponse(AgentSession session)
    {
        return new AgentSessionResponse
        {
            Id = session.Id,
            UserId = session.UserId,
            ProjectId = session.ProjectId,
            Title = session.Title,
            Phase = "idle", // Phase is derived from session data
            IsArchived = session.IsArchived,
            SessionData = session.SessionData ?? "{}",
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt
        };
    }
}

using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.AgentSessions;

namespace TM.Web.NovelAgentWeb.Services.AgentSessions;

/// <summary>
/// Service for managing agent sessions with user isolation.
/// Sessions store conversation history, working memory, and run tracking.
/// </summary>
public interface IAgentSessionService
{
    /// <summary>
    /// Get or create a session for the current user.
    /// If sessionId is null, creates a new session.
    /// </summary>
    Task<AgentSessionResponse> GetOrCreateSessionAsync(
        string? sessionId,
        string userId,
        string? projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a session by ID with ownership verification.
    /// </summary>
    Task<AgentSessionResponse> GetSessionByIdAsync(
        string sessionId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all sessions for the current user.
    /// Excludes archived sessions by default.
    /// </summary>
    Task<List<AgentSessionResponse>> ListUserSessionsAsync(
        string userId,
        bool isAdmin,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update session metadata (title, archived status).
    /// </summary>
    Task<AgentSessionResponse> UpdateSessionAsync(
        string sessionId,
        UpdateAgentSessionRequest request,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save session state (working memory, chat history, run history).
    /// </summary>
    Task SaveSessionStateAsync(
        string sessionId,
        string sessionData,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a session with ownership verification.
    /// </summary>
    Task DeleteSessionAsync(
        string sessionId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);
}

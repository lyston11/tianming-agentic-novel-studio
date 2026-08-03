using TM.Web.NovelAgentWeb.Models.AgentSessions;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentSessions;

/// <summary>
/// 会话应用层唯一入口：聚合命令、查询和实时事件网关。底层持久化组件不再由 Controller/Director 直接访问。
/// </summary>
public interface IAgentSessionApplicationService : IAgentSessionService
{
    Task<AgentSession> GetRuntimeSessionAsync(string? sessionId = null, CancellationToken ct = default);
    Task<AgentSession?> FindRuntimeSessionAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentSession>> ListRuntimeSessionsAsync(CancellationToken ct = default);
    Task SaveRuntimeSessionAsync(AgentSession session, CancellationToken ct = default);
    Task<bool> ReplaceLastAssistantTurnAsync(AgentSession session, string expected, string replacement, CancellationToken ct = default);
    AgentSseSubscription SubscribeEvents(string userId, string sessionId, bool includeBacklog = true);
    Task RemoveRuntimeSessionAsync(string sessionId, CancellationToken ct = default);
}

public sealed class AgentSessionApplicationService : IAgentSessionApplicationService
{
    private readonly AgentSessionManager _runtime;
    private readonly AgentSessionService _persistence;

    public AgentSessionApplicationService(AgentSessionManager runtime, AgentSessionService persistence)
    {
        _runtime = runtime;
        _persistence = persistence;
    }

    public Task<AgentSession> GetRuntimeSessionAsync(string? sessionId = null, CancellationToken ct = default) =>
        _runtime.GetOrCreateSessionAsync(sessionId, ct);
    public Task<AgentSession?> FindRuntimeSessionAsync(string sessionId, CancellationToken ct = default) =>
        _runtime.GetSessionAsync(sessionId, ct);
    public Task<IReadOnlyList<AgentSession>> ListRuntimeSessionsAsync(CancellationToken ct = default) =>
        _runtime.ListSessionsAsync(ct);
    public Task SaveRuntimeSessionAsync(AgentSession session, CancellationToken ct = default) =>
        _runtime.SaveSessionAsync(session, ct);
    public Task<bool> ReplaceLastAssistantTurnAsync(AgentSession session, string expected, string replacement, CancellationToken ct = default) =>
        _runtime.ReplaceLastAssistantTurnAsync(session, expected, replacement, ct);
    public AgentSseSubscription SubscribeEvents(string userId, string sessionId, bool includeBacklog = true) =>
        _runtime.SubscribeEvents(userId, sessionId, includeBacklog);
    public Task RemoveRuntimeSessionAsync(string sessionId, CancellationToken ct = default) =>
        _runtime.RemoveSessionAsync(sessionId, ct);

    public Task<AgentSessionResponse> GetOrCreateSessionAsync(string? sessionId, string userId, string? projectId, string? idempotencyKey, CancellationToken cancellationToken = default) =>
        _persistence.GetOrCreateSessionAsync(sessionId, userId, projectId, idempotencyKey, cancellationToken);
    public Task<AgentSessionResponse> GetSessionByIdAsync(string sessionId, string userId, bool isAdmin, CancellationToken cancellationToken = default) =>
        _persistence.GetSessionByIdAsync(sessionId, userId, isAdmin, cancellationToken);
    public Task<List<AgentSessionResponse>> ListUserSessionsAsync(string userId, bool isAdmin, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        _persistence.ListUserSessionsAsync(userId, isAdmin, includeArchived, cancellationToken);
    public Task<AgentSessionResponse> UpdateSessionAsync(string sessionId, UpdateAgentSessionRequest request, string userId, bool isAdmin, CancellationToken cancellationToken = default) =>
        _persistence.UpdateSessionAsync(sessionId, request, userId, isAdmin, cancellationToken);
    public Task DeleteSessionAsync(string sessionId, string userId, bool isAdmin, CancellationToken cancellationToken = default) =>
        _persistence.DeleteSessionAsync(sessionId, userId, isAdmin, cancellationToken);
}

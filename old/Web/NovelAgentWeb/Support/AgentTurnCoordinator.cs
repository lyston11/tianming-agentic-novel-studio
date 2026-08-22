using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentSessions;

namespace TM.Web.NovelAgentWeb.Support;

/// <summary>
/// Adapts the public chat endpoint to the target-architecture conversation director.
/// Production work is authorized and scheduled through the Goal workflow, never through
/// a legacy runtime run created from a chat turn.
/// </summary>
public sealed class AgentTurnCoordinator
{
    private readonly IAgentForegroundTurnRunner _foreground;
    private readonly IAgentChatIdempotencyService _idempotency;

    public AgentTurnCoordinator(
        IAgentForegroundTurnRunner foreground,
        IAgentChatIdempotencyService idempotency)
    {
        _foreground = foreground;
        _idempotency = idempotency;
    }

    public AgentTurnCoordinator(IAgentForegroundTurnRunner foreground)
        : this(foreground, new PassthroughChatIdempotencyService())
    {
    }

    public async Task<AgentChatResponse> HandleAsync(
        string sessionId,
        string userMessage,
        CancellationToken ct,
        string? idempotencyKey = null,
        string? sourceMessageId = null)
    {
        var canonicalKey = string.IsNullOrWhiteSpace(idempotencyKey) ? sourceMessageId : idempotencyKey;
        return await _idempotency.ExecuteAsync(
            sessionId,
            userMessage,
            canonicalKey,
            async () =>
            {
                var result = await _foreground.TryHandleAsync(sessionId, userMessage, canonicalKey, ct).ConfigureAwait(false);
                return result.Response ?? throw new InvalidOperationException(
                    "Conversation director returned no response. Production work must be started through the Goal workflow.");
            },
            ct).ConfigureAwait(false);
    }

    private sealed class PassthroughChatIdempotencyService : IAgentChatIdempotencyService
    {
        public Task<AgentChatResponse> ExecuteAsync(
            string sessionId,
            string message,
            string? canonicalKey,
            Func<Task<AgentChatResponse>> execute,
            CancellationToken cancellationToken = default) => execute();
    }
}

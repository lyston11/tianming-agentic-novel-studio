using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Support;

/// <summary>
/// Adapts the public chat endpoint to the target-architecture conversation director.
/// Production work is authorized and scheduled through the Goal workflow, never through
/// a legacy runtime run created from a chat turn.
/// </summary>
public sealed class AgentTurnCoordinator
{
    private readonly IAgentForegroundTurnRunner _foreground;

    public AgentTurnCoordinator(IAgentForegroundTurnRunner foreground)
    {
        _foreground = foreground;
    }

    public async Task<AgentChatResponse> HandleAsync(
        string sessionId,
        string userMessage,
        CancellationToken ct,
        string? idempotencyKey = null,
        string? sourceMessageId = null)
    {
        var result = await _foreground.TryHandleAsync(sessionId, userMessage, ct).ConfigureAwait(false);
        return result.Response ?? throw new InvalidOperationException(
            "Conversation director returned no response. Production work must be started through the Goal workflow.");
    }
}

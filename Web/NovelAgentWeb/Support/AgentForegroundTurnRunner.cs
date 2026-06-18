using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Support;

public interface IAgentForegroundTurnRunner
{
    Task<AgentForegroundTurnResult> TryHandleAsync(string sessionId, string userMessage, CancellationToken ct);
}

public sealed record AgentForegroundTurnResult(AgentChatResponse? Response, bool StartBackground)
{
    public static AgentForegroundTurnResult Reply(AgentChatResponse response) => new(response, false);
    public static AgentForegroundTurnResult Background() => new(null, true);
    public static AgentForegroundTurnResult NoBackground(AgentChatResponse response) => new(response, false);
}

public sealed class AgentForegroundTurnRunner : IAgentForegroundTurnRunner
{
    private readonly AgentRuntime _runtime;

    public AgentForegroundTurnRunner(AgentRuntime runtime)
    {
        _runtime = runtime;
    }

    public Task<AgentForegroundTurnResult> TryHandleAsync(string sessionId, string userMessage, CancellationToken ct) =>
        _runtime.TryHandleForegroundAsync(sessionId, userMessage, ct);
}

using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentRouter
{
    private readonly AgentRuntime _runtime;

    public AgentRouter(AgentRuntime runtime) => _runtime = runtime;

    public Task<AgentChatResponse> HandleAsync(
        string sessionId,
        string userMessage,
        CancellationToken ct) =>
        _runtime.RunAsync(sessionId, userMessage, ct);
}

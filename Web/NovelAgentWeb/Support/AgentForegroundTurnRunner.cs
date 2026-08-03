using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Support;

public interface IAgentForegroundTurnRunner
{
    Task<AgentForegroundTurnResult> TryHandleAsync(
        string sessionId,
        string userMessage,
        string? canonicalMessageKey,
        CancellationToken ct);
}

public sealed record AgentForegroundTurnResult(AgentChatResponse? Response, bool StartBackground)
{
    public static AgentForegroundTurnResult Reply(AgentChatResponse response) => new(response, false);
    public static AgentForegroundTurnResult Background() => new(null, true);
    public static AgentForegroundTurnResult NoBackground(AgentChatResponse response) => new(response, false);
}

public interface IAgentInterruptDecisionService
{
    Task<AgentInterruptDecision> DecideAsync(
        AgentSession session,
        Data.Entities.AgentRuntimeRun activeRun,
        string userMessage,
        CancellationToken ct = default);
}

public sealed record AgentInterruptDecision(
    string Kind,
    int Priority,
    string Reason,
    string UserVisibleAcknowledgement)
{
    public static AgentInterruptDecision Freeform(string reason = "") =>
        new("freeform", 0, reason, string.Empty);
}

public interface IAgentBackgroundRunReadinessGate
{
    Task<AgentBackgroundRunReadiness> CheckAsync(
        string sessionId,
        string userMessage,
        CancellationToken ct = default);
}

public sealed record AgentBackgroundRunReadiness(
    bool CanStart,
    string Phase,
    string Reply,
    IReadOnlyList<string> Suggestions)
{
    public static AgentBackgroundRunReadiness Ready() =>
        new(true, "ready", string.Empty, Array.Empty<string>());

    public static AgentBackgroundRunReadiness Blocked(
        string phase,
        string reply,
        IReadOnlyList<string>? suggestions = null) =>
        new(false, phase, reply, suggestions ?? Array.Empty<string>());
}

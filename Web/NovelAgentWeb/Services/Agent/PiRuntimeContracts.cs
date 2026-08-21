using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;

namespace TM.Web.NovelAgentWeb.Services.Agent;

public sealed record PiRuntimeBinding(
    string State,
    string? ProjectId,
    string Version);

public sealed record PiRuntimeContextPayload(
    PiRuntimeBinding Binding,
    string SystemInstructions,
    IReadOnlyList<ConversationRuntimeMessage> DurableMessages,
    object? ProjectContext,
    IReadOnlyList<string> AllowedProjectTools);

public sealed record PiRuntimeTurnRequest(
    string UserId,
    string SessionId,
    string Message,
    IReadOnlyList<string> AttachmentIds,
    string CorrelationId,
    string? SourceUserMessageId,
    PiRuntimeBinding Binding,
    string SystemInstructions,
    IReadOnlyList<ConversationRuntimeMessage> DurableMessages,
    object? ProjectContext,
    IReadOnlyList<string> AllowedProjectTools);

public sealed record PiRuntimeCheckpointResponse(
    string Runtime,
    string CheckpointJson);

public sealed record PiRuntimeResultResponse(
    string AssistantMessage,
    string DecisionKind,
    IReadOnlyList<string> TokenDeltas,
    IReadOnlyList<ConversationRuntimeMessage> Messages,
    IReadOnlyList<AgentToolCall> ToolCalls,
    PiRuntimeCheckpointResponse Checkpoint,
    string? Reason = null);

public sealed record PiRuntimeStreamEvent(
    string Type,
    string? Delta,
    PiRuntimeResultResponse? Result,
    string? Error);

public interface IPiRuntimeContextProvider
{
    Task<PiRuntimeContextPayload> BuildAsync(
        string userId,
        string sessionId,
        string? query,
        CancellationToken cancellationToken);
}

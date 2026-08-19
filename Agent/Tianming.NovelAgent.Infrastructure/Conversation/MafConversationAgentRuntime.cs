using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Domain.Goals;

namespace Tianming.NovelAgent.Infrastructure.Conversation;

public interface IMafAgentInvoker
{
    Task<string> RunAsync(
        string userId,
        string projectId,
        string sessionId,
        string message,
        CancellationToken cancellationToken);
}

public interface IMafSessionCheckpointStore
{
    Task<string?> LoadAsync(string userId, string sessionId, CancellationToken cancellationToken);
    Task SaveAsync(
        string userId,
        string projectId,
        string sessionId,
        string checkpointJson,
        CancellationToken cancellationToken);
}

public sealed class MafAIAgentInvoker(AIAgent agent, IMafSessionCheckpointStore checkpoints) : IMafAgentInvoker
{
    public async Task<string> RunAsync(
        string userId,
        string projectId,
        string sessionId,
        string message,
        CancellationToken cancellationToken)
    {
        var checkpoint = await checkpoints.LoadAsync(userId, sessionId, cancellationToken);
        AgentSession session;
        if (checkpoint is null)
        {
            session = await agent.CreateSessionAsync(cancellationToken);
        }
        else
        {
            using var document = JsonDocument.Parse(checkpoint);
            session = await agent.DeserializeSessionAsync(
                document.RootElement,
                jsonSerializerOptions: null,
                cancellationToken);
        }
        var response = await agent.RunAsync(message, session, cancellationToken: cancellationToken);
        var serialized = await agent.SerializeSessionAsync(
            session,
            jsonSerializerOptions: null,
            cancellationToken);
        await checkpoints.SaveAsync(
            userId,
            projectId,
            sessionId,
            serialized.GetRawText(),
            cancellationToken);
        return response.Text;
    }
}

public sealed class MafConversationAgentRuntime(IMafAgentInvoker invoker) : IConversationAgentRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<ConversationRuntimeResult> RunTurnAsync(
        ConversationTurnContext context,
        CancellationToken cancellationToken)
    {
        var response = await invoker.RunAsync(
            context.UserId,
            context.ProjectId,
            context.SessionId,
            BuildContractPrompt(context),
            cancellationToken);
        MafDecision decision;
        try
        {
            decision = JsonSerializer.Deserialize<MafDecision>(response, JsonOptions)
                ?? throw new JsonException("The MAF response was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The conversation runtime returned an invalid decision contract.", exception);
        }

        if (decision.Kind is ConversationDecisionKind.ProposeGoal or ConversationDecisionKind.ProposeRevision
            && decision.Contract is null)
        {
            throw new InvalidOperationException("A proposal decision must include a goal contract.");
        }

        return new ConversationRuntimeResult(
            decision.Message,
            decision.Kind,
            decision.Contract,
            [],
            decision.Reason,
            decision.ToolCalls ?? []);
    }

    private static string BuildContractPrompt(ConversationTurnContext context) =>
        "You are the conversation runtime for a novel production system. "
        + "Discuss intent and return one JSON object with kind, message, reason, optional contract, and optional toolCalls. "
        + "Never claim to start production or write canon. When the user explicitly commits to a proposed goal, "
        + "return a confirm_creative_goal tool call; the Application layer executes it after Proposal persistence. "
        + $"Project: {context.ProjectId}. User message: {context.Message}";

    private sealed record MafDecision(
        ConversationDecisionKind Kind,
        string Message,
        string? Reason,
        GoalContract? Contract,
        IReadOnlyList<AgentToolCall>? ToolCalls);
}

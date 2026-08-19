using System.Text.Json;
using System.Text.Json.Serialization;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Domain.Goals;

namespace Tianming.NovelAgent.Application.Conversation;

public sealed class StructuredConversationAgentRuntime(IConversationTextCompletionPort completion)
    : IConversationAgentRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<ConversationRuntimeResult> RunTurnAsync(
        ConversationTurnContext context,
        CancellationToken cancellationToken)
    {
        var response = await completion.CompleteAsync(
            context.UserId,
            """
            You are the conversation runtime for a novel production system. Discuss and clarify creative intent.
            Return exactly one JSON object with kind, message, reason, optional contract, and optional toolCalls. Allowed
            kinds are discussOnly, proposeGoal, proposeRevision, needClarification, rejectUnsafe. Never claim that
            production started and never write Canon. A proposal contract must include every GoalContract field. When
            the user explicitly commits to a proposed goal, return proposeGoal with a confirm_creative_goal tool call;
            the Application layer will execute it only after the Proposal is persisted.
            """,
            $"Project: {context.ProjectId}\nUser: {context.Message}",
            cancellationToken);

        RuntimeDecision decision;
        try
        {
            decision = JsonSerializer.Deserialize<RuntimeDecision>(StripFence(response), JsonOptions)
                ?? throw new JsonException("The response was empty.");
        }
        catch (JsonException)
        {
            return new ConversationRuntimeResult(
                response,
                ConversationDecisionKind.DiscussOnly,
                null,
                [],
                "The provider response was not a valid executable decision contract.",
                []);
        }

        if (decision.Kind is ConversationDecisionKind.ProposeGoal or ConversationDecisionKind.ProposeRevision)
        {
            if (decision.Contract is null)
                throw new InvalidOperationException("A proposal decision must include a goal contract.");
            decision.Contract.Validate();
        }
        return new ConversationRuntimeResult(
            decision.Message,
            decision.Kind,
            decision.Contract,
            [],
            decision.Reason,
            decision.ToolCalls ?? []);
    }

    private static string StripFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;
        var firstLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine >= 0 && lastFence > firstLine
            ? trimmed[(firstLine + 1)..lastFence].Trim()
            : trimmed;
    }

    private sealed record RuntimeDecision(
        ConversationDecisionKind Kind,
        string Message,
        string? Reason,
        GoalContract? Contract,
        IReadOnlyList<AgentToolCall>? ToolCalls);
}

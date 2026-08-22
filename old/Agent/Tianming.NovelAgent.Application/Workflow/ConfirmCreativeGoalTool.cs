using System.Text.Json;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;

namespace Tianming.NovelAgent.Application.Workflow;

public sealed class ConfirmCreativeGoalTool(IWorkflowCommandPort workflow) : IAgentTool
{
    public const string ToolName = "confirm_creative_goal";

    public string Name => ToolName;

    public string Description =>
        "Confirm a persisted creative goal and create its planned production workflow after explicit user commitment.";

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolContext context,
        IReadOnlyDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        var proposalId = ReadString(arguments, "proposalId") ?? context.ProposalId;
        if (string.IsNullOrWhiteSpace(proposalId))
            return Failure("A proposalId is required to confirm a creative goal.");

        var confirmationNote = ReadString(arguments, "confirmationNote")
            ?? "Confirmed by the conversation agent after explicit user commitment.";
        try
        {
            var confirmation = await workflow.ConfirmProposalAsync(
                context.UserId,
                proposalId,
                context.UserId,
                new ConfirmGoalProposalRequest(context.IdempotencyKey, confirmationNote),
                cancellationToken);
            return new AgentToolExecutionResult(
                ToolName,
                true,
                $"Creative goal {confirmation.GoalId} is confirmed and its production workflow is ready.",
                confirmation);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return Failure(exception.Message);
        }
    }

    private static AgentToolExecutionResult Failure(string error) =>
        new(ToolName, false, error, Error: error);

    private static string? ReadString(
        IReadOnlyDictionary<string, JsonElement> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}

using System.Text.Json;
using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Application.Workflow;
using Tianming.NovelAgent.Contracts.Conversation;

namespace Tests.AgentArchitecture;

public sealed class AgentToolTests
{
    [Fact]
    public async Task Confirm_tool_injects_the_new_proposal_and_stable_idempotency_key()
    {
        var workflow = new CapturingWorkflow();
        var tool = new ConfirmCreativeGoalTool(workflow);

        var result = await tool.ExecuteAsync(
            new AgentToolContext(
                "user-1",
                "project-1",
                "session-1",
                "proposal-1",
                "conversation:turn-1",
                "correlation-1"),
            new Dictionary<string, JsonElement>(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("proposal-1", workflow.ProposalId);
        Assert.Equal("conversation:turn-1", workflow.Request?.IdempotencyKey);
        Assert.Equal("user-1", workflow.ActorId);
    }

    [Fact]
    public async Task Registry_rejects_unknown_tools_without_calling_workflow()
    {
        var workflow = new CapturingWorkflow();
        var registry = new AgentToolRegistry([new ConfirmCreativeGoalTool(workflow)]);

        var result = await registry.ExecuteAsync(
            new AgentToolCall("not_a_real_tool"),
            new AgentToolContext("user-1", "project-1", "session-1", "proposal-1", "key", "corr"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Error?.Contains("Unknown agent tool", StringComparison.Ordinal) == true);
        Assert.Null(workflow.ProposalId);
    }

    private sealed class CapturingWorkflow : IWorkflowCommandPort
    {
        public string? ProposalId { get; private set; }
        public string? ActorId { get; private set; }
        public ConfirmGoalProposalRequest? Request { get; private set; }

        public Task<ConfirmGoalProposalResult> ConfirmProposalAsync(
            string userId,
            string proposalId,
            string actorId,
            ConfirmGoalProposalRequest request,
            CancellationToken cancellationToken = default)
        {
            ProposalId = proposalId;
            ActorId = actorId;
            Request = request;
            return Task.FromResult(new ConfirmGoalProposalResult(
                "goal-1",
                "revision-1",
                "production-1",
                "correlation-1"));
        }
    }
}

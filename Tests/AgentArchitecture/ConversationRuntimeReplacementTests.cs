using System.Text.Json;
using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Domain.Goals;
using Tianming.NovelAgent.Infrastructure.Conversation;

namespace Tests.AgentArchitecture;

public sealed class ConversationRuntimeReplacementTests
{
    [Fact]
    public async Task Maf_adapter_maps_framework_output_without_leaking_framework_types()
    {
        var contract = NewContract();
        var invoker = new StubInvoker(JsonSerializer.Serialize(new
        {
            kind = "proposeGoal",
            message = "Ready for confirmation",
            reason = "The scope is explicit",
            contract
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        IConversationAgentRuntime runtime = new MafConversationAgentRuntime(invoker);

        var result = await runtime.RunTurnAsync(
            new ConversationTurnContext("user", "project", "session", "Write chapter one", [], "correlation"),
            CancellationToken.None);

        Assert.Equal(ConversationDecisionKind.ProposeGoal, result.DecisionKind);
        Assert.Equivalent(contract, result.ProposedContract, strict: true);
        Assert.DoesNotContain("Microsoft.Agents", typeof(IConversationAgentRuntime).Assembly.GetReferencedAssemblies().Select(x => x.Name));
    }

    [Fact]
    public async Task Maf_adapter_rejects_unstructured_output()
    {
        IConversationAgentRuntime runtime = new MafConversationAgentRuntime(new StubInvoker("not-json"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RunTurnAsync(
            new ConversationTurnContext("user", "project", "session", "hello", [], "correlation"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Maf_adapter_preserves_structured_workflow_tool_calls()
    {
        var response = JsonSerializer.Serialize(new
        {
            kind = "proposeGoal",
            message = "I can start this goal after your confirmation.",
            reason = "The user made an explicit commitment.",
            contract = NewContract(),
            toolCalls = new[]
            {
                new
                {
                    name = "confirm_creative_goal",
                    arguments = new Dictionary<string, string>()
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        IConversationAgentRuntime runtime = new MafConversationAgentRuntime(new StubInvoker(response));

        var result = await runtime.RunTurnAsync(
            new ConversationTurnContext("user", "project", "session", "start", [], "correlation"),
            CancellationToken.None);

        var call = Assert.Single(result.ToolCalls!);
        Assert.Equal("confirm_creative_goal", call.Name);
        Assert.Equal(ConversationDecisionKind.ProposeGoal, result.DecisionKind);
    }

    [Fact]
    public async Task Structured_adapter_maps_workflow_tool_calls()
    {
        var response = JsonSerializer.Serialize(new
        {
            kind = "proposeGoal",
            message = "The goal is ready for confirmation.",
            reason = "Explicit commitment",
            contract = NewContract(),
            toolCalls = new[] { new { name = "confirm_creative_goal", arguments = new { } } }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        IConversationAgentRuntime runtime = new StructuredConversationAgentRuntime(new StubCompletion(response));

        var result = await runtime.RunTurnAsync(
            new ConversationTurnContext("user", "project", "session", "start", [], "correlation"),
            CancellationToken.None);

        Assert.Equal("confirm_creative_goal", Assert.Single(result.ToolCalls!).Name);
    }

    private static GoalContract NewContract() => new(
        "Write chapter one",
        ProductionMode.SingleChapter,
        new ChapterRange(1, 1),
        ["Complete chapter"],
        [],
        [],
        [],
        "human",
        "directed",
        1m,
        "canon-v1",
        "knowledge-v1",
        "quality-v1",
        "style-v1",
        new Dictionary<string, string>(),
        new Dictionary<string, string>());

    private sealed class StubInvoker(string response) : IMafAgentInvoker
    {
        public Task<string> RunAsync(
            string userId,
            string projectId,
            string sessionId,
            string message,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class StubCompletion(string response) : IConversationTextCompletionPort
    {
        public Task<string> CompleteAsync(
            string userId,
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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
            new ConversationTurnContext("user", new BoundConversationBinding("project"), "session", "Write chapter one", [], "correlation"),
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
            new ConversationTurnContext("user", new BoundConversationBinding("project"), "session", "hello", [], "correlation"),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Runtime_adapters_do_not_require_project_contracts_for_unbound_turns(bool useMaf)
    {
        var response = JsonSerializer.Serialize(new
        {
            kind = "proposeGoal",
            message = "Continue clarifying",
            reason = "Malformed project-only decision"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        IConversationAgentRuntime runtime = useMaf
            ? new MafConversationAgentRuntime(new StubInvoker(response))
            : new StructuredConversationAgentRuntime(new StubCompletion(response));

        var result = await runtime.RunTurnAsync(
            new ConversationTurnContext(
                "user",
                new UnboundConversationBinding(),
                "session",
                "hello",
                [],
                "correlation"),
            CancellationToken.None);

        Assert.Equal(ConversationDecisionKind.ProposeGoal, result.DecisionKind);
        Assert.Null(result.ProposedContract);
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
            new ConversationTurnContext(
                "user",
                new BoundConversationBinding("project"),
                "session",
                "start",
                [],
                "correlation"),
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
            new ConversationTurnContext(
                "user",
                new BoundConversationBinding("project"),
                "session",
                "start",
                [],
                "correlation"),
            CancellationToken.None);

        Assert.Equal("confirm_creative_goal", Assert.Single(result.ToolCalls!).Name);
    }

    [Fact]
    public async Task Maf_invoker_returns_checkpoint_that_resumes_after_committed_recreation()
    {
        var checkpoints = new RecordingCheckpointStore();
        var firstClient = new StubChatClient("reply one");
        var firstInvoker = new MafAIAgentInvoker(new ChatClientAgent(firstClient), checkpoints);

        var first = await firstInvoker.RunAsync(
            "user",
            new UnboundConversationBinding(),
            "session",
            "first turn",
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(first.CheckpointJson));
        checkpoints.Commit(first.CheckpointJson);

        var secondClient = new StubChatClient("reply two");
        var restartedInvoker = new MafAIAgentInvoker(new ChatClientAgent(secondClient), checkpoints);
        var second = await restartedInvoker.RunAsync(
            "user",
            new UnboundConversationBinding(),
            "session",
            "second turn",
            CancellationToken.None);

        Assert.Equal("reply two", second.Response);
        Assert.Equal(2, checkpoints.LoadCount);
        var resumedMessages = Assert.Single(secondClient.Requests);
        Assert.Contains(resumedMessages, message => message.Text.Contains("first turn", StringComparison.Ordinal));
        Assert.Contains(resumedMessages, message => message.Text.Contains("reply one", StringComparison.Ordinal));
        Assert.Contains(resumedMessages, message => message.Text.Contains("second turn", StringComparison.Ordinal));
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
        public Task<MafAgentInvocationResult> RunAsync(
            string userId,
            ConversationBinding binding,
            string sessionId,
            string message,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MafAgentInvocationResult(response, "{}"));
    }

    private sealed class RecordingCheckpointStore : IMafSessionCheckpointStore
    {
        public int LoadCount { get; private set; }
        private string? CheckpointJson { get; set; }

        public Task<string?> LoadAsync(
            string userId,
            string sessionId,
            CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(CheckpointJson);
        }

        public void Commit(string checkpointJson) => CheckpointJson = checkpointJson;
    }

    private sealed class StubChatClient(string response) : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
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

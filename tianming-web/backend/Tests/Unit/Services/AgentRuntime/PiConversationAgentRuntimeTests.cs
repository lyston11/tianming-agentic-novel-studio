using System.Net;
using System.Text.Json;
using Moq;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using TM.Web.NovelAgentWeb.Services.Agent;
using Xunit;

namespace Tests.Unit.Services.AgentRuntime;

public sealed class PiConversationAgentRuntimeTests
{
    [Fact]
    public async Task RunTurnAsync_MapsCompletedNdjsonResult()
    {
        var ndjson = string.Join("\n",
            """{"type":"token_delta","delta":"你好"}""",
            """{"type":"tool_start","toolCallId":"tc1","toolName":"list_accessible_projects","argumentsJson":"{}"}""",
            """{"type":"tool_end","toolCallId":"tc1","toolName":"list_accessible_projects","isError":false,"resultJson":"{}"}""",
            """{"type":"completed","result":{"assistantMessage":"完成","decisionKind":"conversation","tokenDeltas":["你好"],"messages":[{"role":"assistant","content":"[]","customType":"pi.assistant.v1"}],"toolCalls":[{"name":"list_accessible_projects","argumentsJson":"{}"}],"checkpoint":{"runtime":"pi-agent-core","checkpointJson":"{}"}}}""");
        var runtime = CreateRuntime(ndjson, out var provider);

        var result = await runtime.RunTurnAsync(UnboundTurn(), CancellationToken.None);

        Assert.Equal("完成", result.AssistantMessage);
        Assert.Equal(ConversationDecisionKind.DiscussOnly, result.DecisionKind);
        Assert.Equal(["你好"], result.TokenDeltas);
        Assert.NotNull(result.Checkpoint);
        Assert.Equal("pi-agent-core", result.Checkpoint!.Runtime);
        Assert.Single(result.Messages!);
        Assert.Equal("pi.assistant.v1", result.Messages![0].CustomType);
        Assert.NotNull(result.ToolCalls);
        Assert.Equal("list_accessible_projects", result.ToolCalls![0].Name);
        Assert.Equal("hello", provider.CapturedQuery);
    }

    [Fact]
    public async Task RunTurnAsync_ThrowsWhenStreamEndsWithoutCompletedEvent()
    {
        var ndjson = """{"type":"failed","error":"model unavailable"}""";
        var runtime = CreateRuntime(ndjson, out _);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RunTurnAsync(UnboundTurn(), CancellationToken.None));

        Assert.Contains("model unavailable", exception.Message);
    }

    [Fact]
    public async Task RunTurnAsync_RejectsBindingChangedBetweenContextBuildAndRequest()
    {
        var handler = new RecordingHandler("""{"type":"completed","result":{}}""");
        var provider = new Mock<IPiRuntimeContextProvider>();
        provider.Setup(port => port.BuildAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PiRuntimeContextPayload(
                new PiRuntimeBinding("bound", "project-9", "3"),
                "sys",
                [],
                null,
                []));
        var runtime = new PiConversationAgentRuntime(
            new HttpClient(handler) { BaseAddress = new Uri("http://pi-runtime.invalid/") },
            provider.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RunTurnAsync(UnboundTurn(), CancellationToken.None));

        Assert.Equal(0, handler.CallCount);
    }

    private static PiConversationAgentRuntime CreateRuntime(string ndjson, out CapturingProvider provider)
    {
        provider = new CapturingProvider();
        return new PiConversationAgentRuntime(
            new HttpClient(new RecordingHandler(ndjson)) { BaseAddress = new Uri("http://pi-runtime.invalid/") },
            provider);
    }

    private static ConversationTurnContext UnboundTurn() => new(
        "user-1",
        new UnboundConversationBinding(),
        "session-1",
        "hello",
        [],
        "corr-1",
        "message-1");

    private sealed class CapturingProvider : IPiRuntimeContextProvider
    {
        public string? CapturedQuery { get; private set; }

        public Task<PiRuntimeContextPayload> BuildAsync(
            string userId,
            string sessionId,
            string? query,
            CancellationToken cancellationToken)
        {
            CapturedQuery = query;
            return Task.FromResult(new PiRuntimeContextPayload(
                new PiRuntimeBinding("unbound", null, "1"),
                "sys",
                [],
                null,
                []));
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string responseBody;

        public RecordingHandler(string responseBody)
        {
            this.responseBody = responseBody;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/x-ndjson"),
            });
        }
    }
}

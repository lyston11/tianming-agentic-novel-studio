using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public sealed class AgentTurnCoordinatorRuntimeQueueTests
{
    [Fact]
    public async Task HandleAsync_ReturnsConversationDirectorResponse()
    {
        var expected = new AgentChatResponse(
            "目标仍在讨论中。",
            ["补充约束"],
            "session-1",
            Phase: "goal_exploring");
        var foreground = new StubForegroundTurnRunner(AgentForegroundTurnResult.Reply(expected));
        var coordinator = new AgentTurnCoordinator(foreground);

        var actual = await coordinator.HandleAsync(
            "session-1",
            "先讨论第三章的目标",
            CancellationToken.None,
            "request-1",
            "message-1");

        Assert.Same(expected, actual);
        Assert.Equal(1, foreground.CallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenDirectorReturnsNoResponse_DoesNotCreateLegacyRuntimeRun()
    {
        var coordinator = new AgentTurnCoordinator(
            new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.HandleAsync("session-1", "开始生产", CancellationToken.None));

        Assert.Contains("Goal workflow", error.Message, StringComparison.Ordinal);
    }

    private sealed class StubForegroundTurnRunner : IAgentForegroundTurnRunner
    {
        private readonly AgentForegroundTurnResult _result;

        public StubForegroundTurnRunner(AgentForegroundTurnResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<AgentForegroundTurnResult> TryHandleAsync(
            string sessionId,
            string userMessage,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}

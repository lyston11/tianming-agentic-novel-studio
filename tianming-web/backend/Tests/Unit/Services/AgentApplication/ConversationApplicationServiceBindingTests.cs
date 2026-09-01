using Moq;
using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Xunit;

namespace Tests.Unit.Services.AgentApplication;

public sealed class ConversationApplicationServiceBindingTests
{
    [Theory]
    [InlineData("missing", false)]
    [InlineData("unowned", false)]
    [InlineData("archived", true)]
    public async Task Invalid_session_fails_before_runtime_or_persistence_dispatch(
        string sessionId,
        bool archived)
    {
        Exception failure = archived
            ? new InvalidOperationException("Archived conversations cannot accept new turns.")
            : new KeyNotFoundException("Conversation was not found.");
        var bindings = new Mock<IConversationSessionBindingReader>(MockBehavior.Strict);
        bindings.Setup(item => item.GetBindingAsync(
                "user-1", sessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        var runtime = new Mock<IConversationAgentRuntime>(MockBehavior.Strict);
        var store = new Mock<IConversationStore>(MockBehavior.Strict);
        var service = new ConversationApplicationService(
            runtime.Object,
            bindings.Object,
            store.Object,
            new Mock<ITransientAgentStream>(MockBehavior.Strict).Object,
            new Mock<IAgentEventWriter>(MockBehavior.Strict).Object,
            new Mock<IAgentUnitOfWork>(MockBehavior.Strict).Object,
            new Mock<IIdGenerator>(MockBehavior.Strict).Object,
            new Mock<IContractHasher>(MockBehavior.Strict).Object,
            new Mock<IClock>(MockBehavior.Strict).Object);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => service.AppendTurnAsync(
            "user-1",
            sessionId,
            new AppendConversationTurnRequest("turn-1", "Hello")));

        Assert.Equal(failure.GetType(), exception.GetType());
        bindings.VerifyAll();
        runtime.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }
}

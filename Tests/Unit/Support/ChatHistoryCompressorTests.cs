using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class ChatHistoryCompressorTests
{
    [Fact]
    public async Task CompressAndPersistAsync_SavesSummariesAndMetaSummaryAfterCompression()
    {
        var repository = new Mock<IChatHistoryRepository>();
        var compressor = new TestChatHistoryCompressor(repository.Object, new LayeredChatHistory
        {
            Summaries =
            {
                new ChatSummary
                {
                    StartTurn = 1,
                    EndTurn = 10,
                    Content = "前十轮摘要",
                    KeyDecisions = new List<string> { "决定一" }
                }
            },
            MetaSummary = "总体摘要"
        });

        var layered = await compressor.CompressAndPersistAsync(
            "user-1",
            "project-1",
            "session-1",
            new List<AgentConversationTurn>(),
            CancellationToken.None);

        Assert.Equal("总体摘要", layered.MetaSummary);
        repository.Verify(x => x.SaveSummaryAsync(
            "user-1",
            "project-1",
            "session-1",
            1,
            10,
            "summary",
            "前十轮摘要",
            It.Is<IReadOnlyList<string>>(d => d.Single() == "决定一"),
            It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(x => x.SaveSummaryAsync(
            "user-1",
            "project-1",
            "session-1",
            1,
            10,
            "meta",
            "总体摘要",
            It.Is<IReadOnlyList<string>>(d => d.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestChatHistoryCompressor : ChatHistoryCompressor
    {
        private readonly LayeredChatHistory _layered;

        public TestChatHistoryCompressor(IChatHistoryRepository chatHistory, LayeredChatHistory layered)
            : base(NullLogger<ChatHistoryCompressor>.Instance, chatHistory)
        {
            _layered = layered;
        }

        protected override Task<LayeredChatHistory> CompressCoreAsync(
            List<AgentConversationTurn> chatHistory,
            CancellationToken ct = default) =>
            Task.FromResult(_layered);
    }
}

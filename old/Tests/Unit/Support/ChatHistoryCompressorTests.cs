using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

    public class ChatHistoryCompressorTests
    {
    [Fact]
    public async Task CompressAsync_CreatesSummaryWhenTenTurnsAreAvailable()
    {
        var compressor = new ChatHistoryCompressor(
            NullLogger<ChatHistoryCompressor>.Instance,
            Mock.Of<IChatHistoryRepository>());
        var history = BuildTurns(10);

        var layered = await compressor.CompressAsync(history, CancellationToken.None);

        Assert.NotEmpty(layered.Summaries);
        Assert.Contains(layered.Summaries, s => s.StartTurn == 1 && s.EndTurn == 10);
        Assert.NotEmpty(layered.Summaries[0].Content);
    }

    [Fact]
    public async Task CompressAsync_DoesNotExtractKeyDecisionsFromMessageKeywords()
    {
        var compressor = new ChatHistoryCompressor(
            NullLogger<ChatHistoryCompressor>.Instance,
            Mock.Of<IChatHistoryRepository>());
        var history = BuildTurns(10);
        history[0].Content = "我决定不要恋爱情绪推进，必须打怪升级。";

        var layered = await compressor.CompressAsync(history, CancellationToken.None);

        var summary = Assert.Single(layered.Summaries);
        Assert.Empty(summary.KeyDecisions);
    }

    [Fact]
    public async Task CompressAsync_CreatesMetaSummaryWhenThirtyTurnsAreAvailable()
    {
        var compressor = new ChatHistoryCompressor(
            NullLogger<ChatHistoryCompressor>.Instance,
            Mock.Of<IChatHistoryRepository>());
        var history = BuildTurns(30);

        var layered = await compressor.CompressAsync(history, CancellationToken.None);

        Assert.NotNull(layered.MetaSummary);
        Assert.Contains("30", layered.MetaSummary);
        Assert.True(layered.Summaries.Count >= 3);
    }

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

    [Fact]
    public async Task CompressAndPersistAsync_DoesNotSaveMetaSummaryWithoutCoveredRange()
    {
        var repository = new Mock<IChatHistoryRepository>();
        var compressor = new TestChatHistoryCompressor(repository.Object, new LayeredChatHistory
        {
            MetaSummary = "总体摘要"
        });

        await compressor.CompressAndPersistAsync(
            "user-1",
            "project-1",
            "session-1",
            new List<AgentConversationTurn>(),
            CancellationToken.None);

        repository.Verify(x => x.SaveSummaryAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
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

    private static List<AgentConversationTurn> BuildTurns(int turnCount)
    {
        var turns = new List<AgentConversationTurn>();
        for (var i = 1; i <= turnCount; i++)
        {
            turns.Add(new AgentConversationTurn
            {
                Role = "user",
                Content = $"第 {i} 轮用户消息，需要记住决定 {i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(i * 2)
            });
            turns.Add(new AgentConversationTurn
            {
                Role = "assistant",
                Content = $"第 {i} 轮助手回复，确认决定 {i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(i * 2 + 1)
            });
        }

        return turns;
    }
}

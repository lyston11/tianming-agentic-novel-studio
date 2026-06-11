using Xunit;
using TM.Web.NovelAgentWeb.Support;

namespace Tests.Unit.Support;

public class LayeredChatHistoryTests
{
    [Fact]
    public void DefaultConstructor_InitializesEmptyCollections()
    {
        var history = new LayeredChatHistory();

        Assert.Null(history.MetaSummary);
        Assert.NotNull(history.Summaries);
        Assert.Empty(history.Summaries);
        Assert.NotNull(history.RecentMessages);
        Assert.Empty(history.RecentMessages);
    }

    [Fact]
    public void CanSetAndGetMetaSummary()
    {
        var history = new LayeredChatHistory
        {
            MetaSummary = "前30轮对话的总结"
        };

        Assert.Equal("前30轮对话的总结", history.MetaSummary);
    }

    [Fact]
    public void CanAddChatSummary()
    {
        var history = new LayeredChatHistory();
        var summary = new ChatSummary
        {
            StartTurn = 1,
            EndTurn = 10,
            Content = "第1-10轮摘要",
            KeyDecisions = new List<string> { "决定使用第三人称视角" },
            CreatedAt = DateTime.UtcNow
        };

        history.Summaries.Add(summary);

        Assert.Single(history.Summaries);
        Assert.Equal("第1-10轮摘要", history.Summaries[0].Content);
    }

    [Fact]
    public void ChatSummary_HasCorrectStructure()
    {
        var summary = new ChatSummary
        {
            StartTurn = 11,
            EndTurn = 20,
            Content = "Test summary",
            KeyDecisions = new List<string> { "Decision 1", "Decision 2" },
            CreatedAt = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc)
        };

        Assert.Equal(11, summary.StartTurn);
        Assert.Equal(20, summary.EndTurn);
        Assert.Equal("Test summary", summary.Content);
        Assert.Equal(2, summary.KeyDecisions.Count);
        Assert.Equal(new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc), summary.CreatedAt);
    }
}

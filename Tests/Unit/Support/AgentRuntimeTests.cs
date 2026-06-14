using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class AgentRuntimeTests
{
    [Fact]
    public void FormatChatPromptWindow_UsesSqlitePromptWindowAndNotSessionSnapshot()
    {
        var promptWindow = new ChatPromptWindowDto(
            "总体摘要",
            new[]
            {
                new ChatHistorySummaryDto(1, 10, "summary", "前十轮摘要", new[] { "决定一" })
            },
            new[]
            {
                new ChatHistoryTurnDto("user", "SQLite 第一条", DateTime.UtcNow),
                new ChatHistoryTurnDto("assistant", "SQLite 回复", DateTime.UtcNow)
            });

        var history = AgentRuntime.FormatChatPromptWindow(promptWindow);

        Assert.Contains("总体摘要", history);
        Assert.Contains("前十轮摘要", history);
        Assert.Contains("决定一", history);
        Assert.Contains("U: SQLite 第一条", history);
        Assert.Contains("A: SQLite 回复", history);
        Assert.DoesNotContain("session snapshot only", history);
    }

    [Fact]
    public void FormatSessionHistorySnapshot_BoundsFallbackHistoryWhenPromptWindowFails()
    {
        var session = new AgentSession();
        for (var i = 0; i < 12; i++)
        {
            session.ChatHistory.Add(new AgentConversationTurn
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"session fallback {i}"
            });
        }

        var history = AgentRuntime.FormatSessionHistorySnapshot(session);
        var lines = history.Split('\n');

        Assert.DoesNotContain(lines, line => line == "U: session fallback 0");
        Assert.DoesNotContain(lines, line => line == "A: session fallback 1");
        Assert.Contains("U: session fallback 2", lines);
        Assert.Contains("A: session fallback 11", lines);
    }
}

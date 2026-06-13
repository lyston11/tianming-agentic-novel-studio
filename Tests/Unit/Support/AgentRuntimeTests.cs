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
}

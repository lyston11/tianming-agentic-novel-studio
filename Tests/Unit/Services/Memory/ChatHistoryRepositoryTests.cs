using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class ChatHistoryRepositoryTests
{
    [Fact]
    public async Task AppendAsync_WritesRedisHotWindowAndSqliteTruth()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var repo = new ChatHistoryRepository(db, redis.Object, memory.Object, NullLogger<ChatHistoryRepository>.Instance);

        await repo.AppendAsync("user-1", "project-1", "session-1", "user", "你好", CancellationToken.None);

        var saved = await db.AgentChatTurns.SingleAsync();
        Assert.Equal("session-1", saved.SessionId);
        Assert.Equal(1, saved.TurnIndex);
        Assert.Equal("你好", saved.Content);
        redis.Verify(x => x.SetAsync(
            It.Is<string>(k => k == "chat:user-1:session-1:hot"),
            It.IsAny<List<ChatHistoryTurnDto>>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPromptWindowAsync_ReturnsMetaSummarySummariesAndRecentMessages()
    {
        await using var db = CreateDb();
        var repo = new ChatHistoryRepository(db, Mock.Of<IDistributedCacheService>(), Mock.Of<IMemoryCacheService>(), NullLogger<ChatHistoryRepository>.Instance);
        await repo.AppendAsync("user-1", "project-1", "session-1", "user", "第一条", CancellationToken.None);
        await repo.SaveSummaryAsync("user-1", "project-1", "session-1", 1, 10, "summary", "前十轮摘要", new[] { "决定一" }, CancellationToken.None);
        await repo.SaveSummaryAsync("user-1", "project-1", "session-1", 1, 30, "meta", "总体摘要", Array.Empty<string>(), CancellationToken.None);

        var window = await repo.GetPromptWindowAsync("user-1", "project-1", "session-1", CancellationToken.None);

        Assert.Equal("总体摘要", window.MetaSummary);
        Assert.Contains(window.Summaries, s => s.Content == "前十轮摘要");
        Assert.Contains(window.RecentMessages, m => m.Content == "第一条");
    }

    [Fact]
    public async Task AppendAsync_IncrementsTurnIndexAndPreservesTrimmedTruthBeyondSnapshotSize()
    {
        await using var db = CreateDb();
        var repo = new ChatHistoryRepository(db, Mock.Of<IDistributedCacheService>(), Mock.Of<IMemoryCacheService>(), NullLogger<ChatHistoryRepository>.Instance);

        for (var i = 0; i < 45; i++)
        {
            await repo.AppendAsync("user-1", null, "session-1", i % 2 == 0 ? "user" : "assistant", $" 内容 {i} ", CancellationToken.None);
        }

        Assert.Equal(45, await db.AgentChatTurns.CountAsync());
        Assert.Equal(45, await db.AgentChatTurns.MaxAsync(t => t.TurnIndex));
        Assert.Contains(await db.AgentChatTurns.ToListAsync(), t => t.TurnIndex == 1 && t.Content == "内容 0");
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}

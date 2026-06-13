using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class ChatHistoryRepositoryTests
{
    private const string ChatSummaryRangeUniquenessMigration = "20260613160459_EnforceChatSummaryRangeUniqueness";

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

    [Fact]
    public async Task AppendAsync_ConcurrentSqliteAppendsForSameSessionKeepContiguousIndexes()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        var options = CreateSqliteOptions(connection.ConnectionString);

        await using (var setupDb = new NovelAgentDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            SeedUserProjectAndSession(setupDb);
            await setupDb.SaveChangesAsync();
        }

        const int appendCount = 16;
        var tasks = Enumerable.Range(0, appendCount).Select(async i =>
        {
            await using var db = new NovelAgentDbContext(options);
            var repo = new ChatHistoryRepository(
                db,
                Mock.Of<IDistributedCacheService>(),
                Mock.Of<IMemoryCacheService>(),
                NullLogger<ChatHistoryRepository>.Instance);

            await repo.AppendAsync("user-1", "project-1", "session-1", i % 2 == 0 ? "user" : "assistant", $"消息 {i}", CancellationToken.None);
        });

        await Task.WhenAll(tasks);

        await using var verifyDb = new NovelAgentDbContext(options);
        var indexes = await verifyDb.AgentChatTurns
            .Where(t => t.SessionId == "session-1")
            .OrderBy(t => t.TurnIndex)
            .Select(t => t.TurnIndex)
            .ToListAsync();

        Assert.Equal(appendCount, indexes.Count);
        Assert.Equal(Enumerable.Range(1, appendCount), indexes);
    }

    [Fact]
    public async Task SaveSummaryAsync_UpsertsSameSessionTypeAndRange()
    {
        await using var db = CreateDb();
        var repo = new ChatHistoryRepository(db, Mock.Of<IDistributedCacheService>(), Mock.Of<IMemoryCacheService>(), NullLogger<ChatHistoryRepository>.Instance);

        await repo.SaveSummaryAsync("user-1", "project-1", "session-1", 1, 10, "summary", "旧摘要", new[] { "旧决定" }, CancellationToken.None);
        await repo.SaveSummaryAsync("user-1", "project-1", "session-1", 1, 10, "summary", "新摘要", new[] { "新决定" }, CancellationToken.None);

        var saved = await db.AgentChatSummaries.SingleAsync();
        Assert.Equal("新摘要", saved.Content);
        var decisions = JsonSerializer.Deserialize<List<string>>(saved.KeyDecisionsJson!) ?? new();
        Assert.Equal(new[] { "新决定" }, decisions);
    }

    [Theory]
    [InlineData("project-1")]
    [InlineData(null)]
    public async Task SaveSummaryAsync_ConcurrentSqliteSavesForSameRangeKeepSingleRow(string? projectId)
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        var options = CreateSqliteOptions(connection.ConnectionString);
        var sessionId = projectId == null ? "session-global" : "session-1";

        await using (var setupDb = new NovelAgentDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            SeedUserProjectAndSession(setupDb);
            setupDb.AgentSessions.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentSession { Id = "session-global", UserId = "user-1", Title = "Global Session" });
            await setupDb.SaveChangesAsync();
        }

        const int saveCount = 16;
        var tasks = Enumerable.Range(0, saveCount).Select(async i =>
        {
            await using var db = new NovelAgentDbContext(options);
            var repo = new ChatHistoryRepository(
                db,
                Mock.Of<IDistributedCacheService>(),
                Mock.Of<IMemoryCacheService>(),
                NullLogger<ChatHistoryRepository>.Instance);

            await repo.SaveSummaryAsync("user-1", projectId, sessionId, 1, 10, "summary", $"摘要 {i}", new[] { $"决定 {i}" }, CancellationToken.None);
        });

        await Task.WhenAll(tasks);

        await using var verifyDb = new NovelAgentDbContext(options);
        var rows = await verifyDb.AgentChatSummaries
            .Where(s => s.UserId == "user-1" &&
                        s.ProjectId == projectId &&
                        s.SessionId == sessionId &&
                        s.SummaryType == "summary" &&
                        s.StartTurn == 1 &&
                        s.EndTurn == 10)
            .ToListAsync();

        Assert.Single(rows);
        Assert.StartsWith("摘要 ", rows[0].Content);
    }

    [Theory]
    [InlineData("project-1")]
    [InlineData(null)]
    public async Task AgentChatSummariesSqliteSchema_RejectsDuplicateSummaryRanges(string? projectId)
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        var options = CreateSqliteOptions(connection.ConnectionString);
        var sessionId = projectId == null ? "session-global" : "session-1";

        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProjectAndSession(db);
        db.AgentSessions.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentSession { Id = "session-global", UserId = "user-1", Title = "Global Session" });
        await db.SaveChangesAsync();

        db.AgentChatSummaries.Add(CreateSummary("summary-a", projectId, sessionId, "旧摘要"));
        await db.SaveChangesAsync();
        db.AgentChatSummaries.Add(CreateSummary("summary-b", projectId, sessionId, "新摘要"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ChatSummaryRangeUniquenessMigration_AppliesFilteredUniqueIndexes()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        var options = CreateSqliteOptions(connection.ConnectionString);

        await using var db = new NovelAgentDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync(ChatSummaryRangeUniquenessMigration);

        SeedUserProjectAndSession(db);
        await db.SaveChangesAsync();
        db.AgentChatSummaries.Add(CreateSummary("summary-a", "project-1", "session-1", "旧摘要"));
        await db.SaveChangesAsync();
        db.AgentChatSummaries.Add(CreateSummary("summary-b", "project-1", "session-1", "新摘要"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task<SqliteConnection> CreateOpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared");
        await connection.OpenAsync();
        return connection;
    }

    private static DbContextOptions<NovelAgentDbContext> CreateSqliteOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connectionString)
            .Options;
    }

    private static void SeedUserProjectAndSession(NovelAgentDbContext db)
    {
        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.NovelProjects.Add(new NovelProject { Id = "project-1", UserId = "user-1", Title = "Project" });
        db.AgentSessions.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentSession { Id = "session-1", UserId = "user-1", ProjectId = "project-1", Title = "Session" });
    }

    private static AgentChatSummary CreateSummary(string id, string? projectId, string sessionId, string content) =>
        new()
        {
            Id = id,
            UserId = "user-1",
            ProjectId = projectId,
            SessionId = sessionId,
            StartTurn = 1,
            EndTurn = 10,
            SummaryType = "summary",
            Content = content,
            CreatedAt = DateTime.UtcNow
        };
}

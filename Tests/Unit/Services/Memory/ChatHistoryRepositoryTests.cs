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
        private const string UnifiedMemoryMigration = "20260613054958_AddUnifiedMemoryPipeline";
        private const string ChatSummaryRangeUniquenessMigration = "20260613160459_EnforceChatSummaryRangeUniqueness";
        private const string HotWindowKey = "chat:user-1:session-1:project-1:hot";
        private const string SummaryKey = "chat:user-1:session-1:project-1:summaries";

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
            It.Is<string>(k => k == HotWindowKey),
            It.IsAny<List<ChatHistoryTurnDto>>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AppendAsync_BumpsChatMemoryVersionForSessionContext()
    {
        await using var db = CreateDb();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.BumpAsync("user-1", "project-1", "session-1", "chat", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var repo = new ChatHistoryRepository(
            db,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>(),
            NullLogger<ChatHistoryRepository>.Instance,
            versions.Object);

        await repo.AppendAsync("user-1", "project-1", "session-1", "assistant", "恢复记忆", CancellationToken.None);

        versions.Verify(x => x.BumpAsync("user-1", "project-1", "session-1", "chat", It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task GetPromptWindowAsync_UsesRedisHotWindowBeforeSqliteRecentTurns()
    {
        await using var db = CreateDb();
        db.AgentChatTurns.Add(new AgentChatTurn
        {
            Id = "turn-sqlite",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            TurnIndex = 1,
            Role = "user",
            Content = "sqlite old",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        memory.Setup(x => x.Get<List<ChatHistoryTurnDto>>(HotWindowKey)).Returns((List<ChatHistoryTurnDto>?)null);
        redis.Setup(x => x.GetAsync<List<ChatHistoryTurnDto>>(HotWindowKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatHistoryTurnDto>
            {
                new("user", "redis hot", DateTime.UtcNow)
            });

        var repo = new ChatHistoryRepository(db, redis.Object, memory.Object, NullLogger<ChatHistoryRepository>.Instance);

        var window = await repo.GetPromptWindowAsync("user-1", "project-1", "session-1", CancellationToken.None);

        Assert.DoesNotContain(window.RecentMessages, m => m.Content == "sqlite old");
        Assert.Contains(window.RecentMessages, m => m.Content == "redis hot");
        memory.Verify(x => x.Set(HotWindowKey, It.IsAny<List<ChatHistoryTurnDto>>(), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task GetPromptWindowAsync_FiltersHotWindowFallbackByProject()
    {
        await using var db = CreateDb();
        db.AgentChatTurns.AddRange(
            new AgentChatTurn
            {
                Id = "turn-unbound",
                UserId = "user-1",
                ProjectId = null,
                SessionId = "session-1",
                TurnIndex = 0,
                Role = "user",
                Content = "unbound session memory",
                CreatedAt = DateTime.UtcNow
            },
            new AgentChatTurn
            {
                Id = "turn-project-1",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                TurnIndex = 1,
                Role = "user",
                Content = "project one memory",
                CreatedAt = DateTime.UtcNow
            },
            new AgentChatTurn
            {
                Id = "turn-project-2",
                UserId = "user-1",
                ProjectId = "project-2",
                SessionId = "session-1",
                TurnIndex = 2,
                Role = "user",
                Content = "project two memory",
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var repo = new ChatHistoryRepository(
            db,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>(),
            NullLogger<ChatHistoryRepository>.Instance);

        var window = await repo.GetPromptWindowAsync("user-1", "project-1", "session-1", CancellationToken.None);

        Assert.Contains(window.RecentMessages, m => m.Content == "unbound session memory");
        Assert.Contains(window.RecentMessages, m => m.Content == "project one memory");
        Assert.DoesNotContain(window.RecentMessages, m => m.Content == "project two memory");
    }

    [Fact]
    public async Task GetHotWindowAsync_BridgesUnboundTurnsAfterProjectIsSelected()
    {
        await using var db = CreateDb();
        db.AgentChatTurns.AddRange(
            new AgentChatTurn
            {
                Id = "turn-unbound",
                UserId = "user-1",
                ProjectId = null,
                SessionId = "session-1",
                TurnIndex = 1,
                Role = "user",
                Content = "我想先聊一个新故事方向",
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new AgentChatTurn
            {
                Id = "turn-project",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                TurnIndex = 2,
                Role = "assistant",
                Content = "已经切到项目继续规划",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new AgentChatTurn
            {
                Id = "turn-other-project",
                UserId = "user-1",
                ProjectId = "project-2",
                SessionId = "session-1",
                TurnIndex = 3,
                Role = "assistant",
                Content = "其他项目的上下文",
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var repo = new ChatHistoryRepository(
            db,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>(),
            NullLogger<ChatHistoryRepository>.Instance);

        var turns = await repo.GetHotWindowAsync("user-1", "project-1", "session-1", CancellationToken.None);

        Assert.Contains(turns, t => t.Content == "我想先聊一个新故事方向");
        Assert.Contains(turns, t => t.Content == "已经切到项目继续规划");
        Assert.DoesNotContain(turns, t => t.Content == "其他项目的上下文");
    }

    [Fact]
    public async Task SaveSummaryAsync_InvalidatesSummaryCache()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        redis.Setup(x => x.RemoveAsync(SummaryKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var repo = new ChatHistoryRepository(db, redis.Object, memory.Object, NullLogger<ChatHistoryRepository>.Instance);

        await repo.SaveSummaryAsync(
            "user-1",
            "project-1",
            "session-1",
            1,
            10,
            "summary",
            "摘要",
            Array.Empty<string>(),
            CancellationToken.None);

        memory.Verify(x => x.Remove(SummaryKey), Times.Once);
        redis.Verify(x => x.RemoveAsync(SummaryKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveSummaryAsync_BumpsChatMemoryVersionForCompressedPromptWindow()
    {
        await using var db = CreateDb();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.BumpAsync("user-1", "project-1", "session-1", "chat", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var repo = new ChatHistoryRepository(
            db,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>(),
            NullLogger<ChatHistoryRepository>.Instance,
            versions.Object);

        await repo.SaveSummaryAsync(
            "user-1",
            "project-1",
            "session-1",
            1,
            10,
            "summary",
            "压缩摘要",
            Array.Empty<string>(),
            CancellationToken.None);

        versions.Verify(x => x.BumpAsync("user-1", "project-1", "session-1", "chat", It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task AppendAsync_CreatesMissingSessionBeforeFirstTurnOnSqlite()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        var options = CreateSqliteOptions(connection.ConnectionString);

        await using (var setupDb = new NovelAgentDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            setupDb.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
            await setupDb.SaveChangesAsync();
        }

        await using var db = new NovelAgentDbContext(options);
        var repo = new ChatHistoryRepository(
            db,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>(),
            NullLogger<ChatHistoryRepository>.Instance);

        await repo.AppendAsync("user-1", null, "session-new", "user", "第一条消息", CancellationToken.None);

        Assert.True(await db.AgentSessions.AnyAsync(s => s.Id == "session-new" && s.UserId == "user-1"));
        Assert.True(await db.AgentChatTurns.AnyAsync(t => t.SessionId == "session-new" && t.TurnIndex == 1));
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
        InsertSqliteSession(db, "session-global", projectId: null, title: "Global Session");
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

    [Fact]
    public async Task ChatSummaryRangeUniquenessMigration_DeduplicatesExistingSummaryRangesBeforeCreatingIndexes()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        var options = CreateSqliteOptions(connection.ConnectionString);

        await using (var db = new NovelAgentDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(UnifiedMemoryMigration);
            SeedUserProjectAndSession(db);
            InsertSqliteSession(db, "session-global", projectId: null, title: "Global Session");
            db.AgentChatSummaries.AddRange(
                CreateSummary("project-old", "project-1", "session-1", "项目旧摘要", new DateTime(2026, 6, 13, 8, 0, 0, DateTimeKind.Utc)),
                CreateSummary("project-new", "project-1", "session-1", "项目新摘要", new DateTime(2026, 6, 13, 8, 1, 0, DateTimeKind.Utc)),
                CreateSummary("global-old", null, "session-global", "全局旧摘要", new DateTime(2026, 6, 13, 8, 0, 0, DateTimeKind.Utc)),
                CreateSummary("global-new", null, "session-global", "全局新摘要", new DateTime(2026, 6, 13, 8, 1, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        await using (var db = new NovelAgentDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(ChatSummaryRangeUniquenessMigration);
        }

        await using (var verifyDb = new NovelAgentDbContext(options))
        {
            var projectRows = await verifyDb.AgentChatSummaries
                .Where(s => s.UserId == "user-1" &&
                            s.ProjectId == "project-1" &&
                            s.SessionId == "session-1" &&
                            s.SummaryType == "summary" &&
                            s.StartTurn == 1 &&
                            s.EndTurn == 10)
                .ToListAsync();
            var globalRows = await verifyDb.AgentChatSummaries
                .Where(s => s.UserId == "user-1" &&
                            s.ProjectId == null &&
                            s.SessionId == "session-global" &&
                            s.SummaryType == "summary" &&
                            s.StartTurn == 1 &&
                            s.EndTurn == 10)
                .ToListAsync();

            Assert.Single(projectRows);
            Assert.Single(globalRows);
            Assert.Equal("项目新摘要", projectRows[0].Content);
            Assert.Equal("全局新摘要", globalRows[0].Content);
        }
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
        if (db.Database.IsSqlite())
        {
            db.Database.ExecuteSqlRaw("""
                INSERT INTO users (id, username, email, password_hash, role)
                VALUES ('user-1', 'u', 'u@example.com', 'h', 'author');
                """);
            db.Database.ExecuteSqlRaw("""
                INSERT INTO novel_projects (id, user_id, title)
                VALUES ('project-1', 'user-1', 'Project');
                """);
            db.Database.ExecuteSqlRaw("""
                INSERT INTO agent_sessions (id, user_id, project_id, title)
                VALUES ('session-1', 'user-1', 'project-1', 'Session');
                """);
            return;
        }

        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.NovelProjects.Add(new NovelProject { Id = "project-1", UserId = "user-1", Title = "Project" });
        db.AgentSessions.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentSession { Id = "session-1", UserId = "user-1", ProjectId = "project-1", Title = "Session" });
    }

    private static void InsertSqliteSession(NovelAgentDbContext db, string sessionId, string? projectId, string title)
    {
        if (db.Database.IsSqlite())
        {
            if (projectId == null)
            {
                db.Database.ExecuteSqlRaw(
                    """
                    INSERT INTO agent_sessions (id, user_id, project_id, title)
                    VALUES ({0}, 'user-1', NULL, {1});
                    """,
                    sessionId,
                    title);
                return;
            }

            db.Database.ExecuteSqlRaw(
                """
                INSERT INTO agent_sessions (id, user_id, project_id, title)
                VALUES ({0}, 'user-1', {1}, {2});
                """,
                sessionId,
                projectId,
                title);
            return;
        }

        db.AgentSessions.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentSession
        {
            Id = sessionId,
            UserId = "user-1",
            ProjectId = projectId,
            Title = title
        });
    }

    private static AgentChatSummary CreateSummary(string id, string? projectId, string sessionId, string content, DateTime? createdAt = null) =>
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
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
}

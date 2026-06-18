using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class ToolSearchCacheServiceTests
{
    [Fact]
    public async Task SaveAsync_UsesCanonicalVersionedRedisKey()
    {
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=1|session=2|execution=3");
        await using var db = CreateDb();
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };
        var expectedKey = AgentMemoryKeys.ToolCache(
            "user-1",
            "session-1",
            "project-1",
            "global:Planning",
            "project=1|session=2");

        await service.SaveAsync(
            session,
            "Planning",
            new[] { new ToolSchema { Name = "PlanChapter" } },
            CancellationToken.None);

        var setInvocation = Assert.Single(redis.Invocations.Where(i =>
            i.Method.Name == nameof(IDistributedCacheService.SetAsync) &&
            i.Arguments[0] is string key &&
            key == expectedKey));
        Assert.Equal(TimeSpan.FromMinutes(5), setInvocation.Arguments[2]);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenMemoryVersionChanged()
    {
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=2|execution=1");

        await using var db = CreateDb();
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "global:Planning",
            ToolSearchCacheVersion = "project=1|execution=1",
            LastToolSearchAt = DateTime.UtcNow,
            DiscoveredTools = new List<ToolSchema>
            {
                new() { Name = "PlanChapter", Description = "规划章节" }
            }
        };

        var result = await service.GetAsync(session, "Planning", CancellationToken.None);

        Assert.False(result.Hit);
        Assert.Null(result.Tools);
        Assert.Equal("none", result.Source);
    }

    [Fact]
    public async Task GetAsync_KeepsToolSearchCacheFreshWhenOnlyToolExecutionVersionChanged()
    {
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project:project-1:*=1|tool_execution:project-1:session-1=7");

        await using var db = CreateDb();
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "global:Planning",
            ToolSearchCacheVersion = "project:project-1:*=1",
            LastToolSearchAt = DateTime.UtcNow,
            DiscoveredTools = new List<ToolSchema>
            {
                new() { Name = "PlanChapter", Description = "规划章节" }
            }
        };

        var result = await service.GetAsync(session, "Planning", CancellationToken.None);

        Assert.True(result.Hit);
        Assert.Equal("session-hot", result.Source);
        Assert.NotNull(result.Tools);
        Assert.Single(result.Tools);
        Assert.Equal("PlanChapter", result.Tools![0].Name);
    }

    [Fact]
    public async Task GetAsync_RestoresToolsFromSqliteSnapshotAndRefreshesRedisOnHotCacheMiss()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=1|session=2|tool_execution=9");
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var originalSession = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        await service.SaveAsync(
            originalSession,
            "Planning",
            new[]
            {
                new ToolSchema
                {
                    Name = "PlanChapter",
                    Description = "规划章节",
                    Risk = "Medium",
                    Parameters = new Dictionary<string, string> { ["creativeBrief"] = "string" }
                }
            },
            CancellationToken.None);

        var reloadedSession = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "global:Planning",
            ToolSearchCacheVersion = "project=1|session=2",
            LastToolSearchAt = originalSession.LastToolSearchAt
        };

        var result = await service.GetAsync(reloadedSession, "Planning", CancellationToken.None);

        Assert.True(result.Hit);
        Assert.Equal("sqlite-snapshot", result.Source);
        Assert.NotNull(result.Tools);
        Assert.Single(result.Tools);
        Assert.Equal("PlanChapter", result.Tools![0].Name);
        Assert.Single(await db.AgentToolSearchSnapshots.ToListAsync());
        redis.Verify(x => x.SetAsync(
                It.Is<string>(key => key == AgentMemoryKeys.ToolCache(
                    "user-1",
                    "session-1",
                    "project-1",
                    "global:Planning",
                    "project=1|session=2")),
                It.IsAny<object>(),
                It.Is<TimeSpan?>(ttl => ttl > TimeSpan.Zero && ttl <= TimeSpan.FromMinutes(5)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetAsync_RestoresToolsFromHotCacheBeforeSqliteSnapshot()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheService>.Instance);
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=1|session=2");
        var service = new ToolSearchCacheService(redis.Object, memory, versions.Object, db);
        var originalSession = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        await service.SaveAsync(
            originalSession,
            "Planning",
            new[] { new ToolSchema { Name = "PlanChapter", Description = "规划章节" } },
            CancellationToken.None);

        var reloadedSession = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "Planning",
            ToolSearchCacheVersion = "project=1|session=2",
            LastToolSearchAt = DateTime.UtcNow.AddMinutes(-10)
        };

        db.AgentToolSearchSnapshots.RemoveRange(db.AgentToolSearchSnapshots);
        await db.SaveChangesAsync();

        var result = await service.GetAsync(reloadedSession, "Planning", CancellationToken.None);

        Assert.True(result.Hit);
        Assert.Equal("redis-hot", result.Source);
        Assert.NotNull(result.Tools);
        Assert.Equal("PlanChapter", result.Tools![0].Name);
        Assert.Empty(await db.AgentToolSearchSnapshots.ToListAsync());
    }

    [Fact]
    public async Task SaveAndGetAsync_NormalizesPhaseCase()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", null, "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session=1");
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1"
        };

        await service.SaveAsync(
            session,
            "planning",
            new[] { new ToolSchema { Name = "ResolveNovelProject" } },
            CancellationToken.None);
        session.DiscoveredTools.Clear();

        var result = await service.GetAsync(session, "Planning", CancellationToken.None);

        Assert.True(result.Hit);
        Assert.Equal("sqlite-snapshot", result.Source);
        Assert.NotNull(result.Tools);
        Assert.Equal("global:Planning", session.DiscoveredPhase);
        Assert.Equal("ResolveNovelProject", result.Tools![0].Name);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}

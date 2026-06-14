using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Repositories;
using Xunit;

namespace Tests.Unit.Services.Repositories;

public sealed class StoryBibleRepositoryCacheTests
{
    private const string UserId = "user-cache";
    private const string ProjectId = "project-cache";

    private readonly NovelAgentDbContext _db;
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IDistributedCacheService> _redis = new();
    private readonly Mock<IMemoryCacheService> _memory = new();
    private readonly Mock<ILogger<StoryBibleRepository>> _logger = new();

    public StoryBibleRepositoryCacheTests()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new NovelAgentDbContext(options);
        _currentUser.Setup(s => s.GetUserId()).Returns(UserId);
        _memory.Setup(c => c.GetOrSetAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<StoryBible>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<Task<StoryBible>> factory, TimeSpan _, CancellationToken _) => factory());
    }

    [Fact]
    public async Task LoadStoryBibleAsync_ReadsRedisBeforeSqlite()
    {
        var cached = new StoryBible
        {
            Constitution = new StoryConstitution
            {
                Id = "cached-constitution",
                UserId = UserId,
                ProjectId = ProjectId,
                Genre = "cached-genre",
                CoreHook = "from redis"
            }
        };
        _redis.Setup(c => c.GetAsync<StoryBible>(
                $"storybible:{UserId}:{ProjectId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var repository = CreateRepository();

        var result = await repository.LoadStoryBibleAsync(ProjectId);

        Assert.Equal("from redis", result.Constitution?.CoreHook);
        Assert.Empty(_db.StoryConstitutions);
        _redis.Verify(c => c.GetAsync<StoryBible>($"storybible:{UserId}:{ProjectId}", It.IsAny<CancellationToken>()), Times.Once);
        _redis.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<StoryBible>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoadStoryBibleAsync_CachesSqliteMissIntoRedis()
    {
        SeedProject();
        _db.StoryConstitutions.Add(new StoryConstitution
        {
            Id = "constitution-db",
            UserId = UserId,
            ProjectId = ProjectId,
            Genre = "fantasy",
            CoreHook = "from sqlite",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _redis.Setup(c => c.GetAsync<StoryBible>(
                $"storybible:{UserId}:{ProjectId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoryBible?)null);

        var repository = CreateRepository();

        var result = await repository.LoadStoryBibleAsync(ProjectId);

        Assert.Equal("from sqlite", result.Constitution?.CoreHook);
        _redis.Verify(c => c.SetAsync(
            $"storybible:{UserId}:{ProjectId}",
            It.Is<StoryBible>(b => b.Constitution != null && b.Constitution.CoreHook == "from sqlite"),
            It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(10)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAgentRunAsync_WritesSqliteRefreshesRunCacheAndInvalidatesStoryBible()
    {
        SeedProject();
        var repository = CreateRepository();
        var run = new AgentRun
        {
            Id = "run-1",
            UserId = UserId,
            ProjectId = ProjectId,
            RunType = "chapter_generation",
            Status = "completed",
            StartedAt = DateTime.UtcNow,
            OutputData = "{\"ok\":true}"
        };

        await repository.SaveAgentRunAsync(run);

        Assert.Single(_db.AgentRuns);
        _memory.Verify(c => c.Remove($"storybible:{UserId}:{ProjectId}"), Times.Once);
        _redis.Verify(c => c.RemoveAsync($"storybible:{UserId}:{ProjectId}", It.IsAny<CancellationToken>()), Times.Once);
        _memory.Verify(c => c.Set(
            $"agentrun:{UserId}:{ProjectId}:run-1",
            It.Is<AgentRun>(r => r.OutputData == "{\"ok\":true}"),
            TimeSpan.FromMinutes(1)), Times.Once);
        _redis.Verify(c => c.SetAsync(
            $"agentrun:{UserId}:{ProjectId}:run-1",
            It.Is<AgentRun>(r => r.OutputData == "{\"ok\":true}"),
            It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(10)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAgentRunAsync_BumpsStoryBibleMemoryVersion()
    {
        SeedProject();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.BumpAsync(UserId, ProjectId, null, "story_bible", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var repository = CreateRepository(versions.Object);
        var run = new AgentRun
        {
            Id = "run-memory-version",
            UserId = UserId,
            ProjectId = ProjectId,
            RunType = "chapter_generation",
            Status = "completed",
            StartedAt = DateTime.UtcNow,
            OutputData = "{\"ok\":true}"
        };

        await repository.SaveAgentRunAsync(run);

        versions.Verify(x => x.BumpAsync(UserId, ProjectId, null, "story_bible", It.IsAny<CancellationToken>()), Times.Once);
    }

    private StoryBibleRepository CreateRepository(IAgentMemoryVersionService? versions = null) =>
        new(
            _db,
            _currentUser.Object,
            _redis.Object,
            _memory.Object,
            _logger.Object,
            versions);

    private void SeedProject()
    {
        _db.Users.Add(new User
        {
            Id = UserId,
            Username = "cache-user",
            Email = "cache@example.test",
            PasswordHash = "hash",
            Role = "author"
        });
        _db.NovelProjects.Add(new NovelProject
        {
            Id = ProjectId,
            UserId = UserId,
            Title = "Cache Project",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
    }
}

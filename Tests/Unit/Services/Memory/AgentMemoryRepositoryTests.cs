using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Data;

namespace Tests.Unit.Services.Memory;

public class AgentMemoryRepositoryTests
{
    private readonly NovelAgentDbContext _dbContext;
    private readonly Mock<IDistributedCacheService> _mockRedisCache;
    private readonly Mock<IMemoryCacheService> _mockMemoryCache;
    private readonly Mock<ILogger<AgentMemoryRepository>> _mockLogger;
    private readonly AgentMemoryRepository _repository;

    public AgentMemoryRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new NovelAgentDbContext(options);
        _mockRedisCache = new Mock<IDistributedCacheService>();
        _mockMemoryCache = new Mock<IMemoryCacheService>();
        _mockLogger = new Mock<ILogger<AgentMemoryRepository>>();

        _repository = new AgentMemoryRepository(
            _dbContext,
            _mockRedisCache.Object,
            _mockMemoryCache.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetProjectMemoryAsync_ReturnsEmptyMemory_WhenNoDataExists()
    {
        var userId = "user123";
        var projectId = "proj456";

        _mockMemoryCache.Setup(x => x.GetOrSetAsync(
            It.IsAny<string>(),
            It.IsAny<Func<Task<ProjectMemory>>>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string k, Func<Task<ProjectMemory>> f, TimeSpan t, CancellationToken c) => f().Result);

        _mockRedisCache.Setup(x => x.GetAsync<ProjectMemory>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMemory?)null);

        var result = await _repository.GetProjectMemoryAsync(userId, projectId);

        Assert.NotNull(result);
        Assert.Null(result.LongTermGoal);
        Assert.Null(result.ReaderPromise);
        Assert.Empty(result.Constraints);
        Assert.Empty(result.UnresolvedThreads);
    }

    [Fact]
    public async Task GetAuthorMemoryAsync_ReturnsEmptyMemory_WhenNoDataExists()
    {
        var userId = "user123";

        _mockMemoryCache.Setup(x => x.GetOrSetAsync(
            It.IsAny<string>(),
            It.IsAny<Func<Task<AuthorMemory>>>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string k, Func<Task<AuthorMemory>> f, TimeSpan t, CancellationToken c) => f().Result);

        _mockRedisCache.Setup(x => x.GetAsync<AuthorMemory>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorMemory?)null);

        var result = await _repository.GetAuthorMemoryAsync(userId);

        Assert.NotNull(result);
        Assert.Empty(result.StyleLikes);
        Assert.Empty(result.StyleDislikes);
        Assert.Null(result.ConfirmationTolerance);
        Assert.Empty(result.GenreHabits);
    }

    [Fact]
    public async Task GetExecutionMemoryAsync_ReturnsEmptyMemory_WhenNoDataExists()
    {
        var userId = "user123";
        var projectId = "proj456";

        _mockMemoryCache.Setup(x => x.GetOrSetAsync(
            It.IsAny<string>(),
            It.IsAny<Func<Task<ExecutionMemory>>>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string k, Func<Task<ExecutionMemory>> f, TimeSpan t, CancellationToken c) => f().Result);

        _mockRedisCache.Setup(x => x.GetAsync<ExecutionMemory>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExecutionMemory?)null);

        var result = await _repository.GetExecutionMemoryAsync(userId, projectId);

        Assert.NotNull(result);
        Assert.Empty(result.ToolFailurePatterns);
        Assert.Empty(result.RepeatedBlockers);
        Assert.Empty(result.SuccessfulRepairNotes);
    }
}

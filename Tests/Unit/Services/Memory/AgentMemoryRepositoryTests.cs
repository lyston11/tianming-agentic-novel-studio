using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using System.Text.Json;

namespace Tests.Unit.Services.Memory;

public class AgentMemoryRepositoryTests
{
    private readonly NovelAgentDbContext _dbContext;
    private readonly Mock<IDistributedCacheService> _mockRedisCache;
    private readonly Mock<IMemoryCacheService> _mockMemoryCache;
    private readonly Mock<IVectorStore> _mockVectorStore;
    private readonly Mock<IMicroEmbeddingService> _mockEmbedding;
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
        _mockVectorStore = new Mock<IVectorStore>();
        _mockEmbedding = new Mock<IMicroEmbeddingService>();
        _mockLogger = new Mock<ILogger<AgentMemoryRepository>>();

        _repository = new AgentMemoryRepository(
            _dbContext,
            _mockRedisCache.Object,
            _mockMemoryCache.Object,
            _mockVectorStore.Object,
            _mockEmbedding.Object,
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

    [Fact]
    public async Task GetSessionMemoryAsync_ReadsRedisThenSqliteAndIncludesUploadedKnowledge()
    {
        var userId = "user123";
        var projectId = "proj456";
        var sessionId = "session789";
        _dbContext.AgentMemories.Add(new AgentMemory
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = projectId,
            SessionId = sessionId,
            MemoryType = "session.recent_uploaded_knowledge_ids",
            MemoryKey = "recent_uploaded_knowledge_ids",
            Content = JsonSerializer.Serialize(new List<string> { "knowledge-1" })
        });
        await _dbContext.SaveChangesAsync();

        _mockMemoryCache.Setup(x => x.GetOrSetAsync(It.IsAny<string>(), It.IsAny<Func<Task<SessionMemory>>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Func<Task<SessionMemory>> f, TimeSpan _, CancellationToken _) => f().Result);
        _mockRedisCache.Setup(x => x.GetAsync<SessionMemory>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionMemory?)null);

        var result = await _repository.GetSessionMemoryAsync(userId, projectId, sessionId);

        Assert.Contains("knowledge-1", result.RecentUploadedKnowledgeIds);
    }

    [Fact]
    public async Task GetProjectMemoryAsync_DeserializesDataCorrectly_WhenDataExists()
    {
        var userId = "user123";
        var projectId = "proj456";

        _dbContext.AgentMemories.AddRange(
            new AgentMemory { Id = Guid.NewGuid().ToString(), UserId = userId, ProjectId = projectId, MemoryType = "project.long_term_goal", Content = JsonSerializer.Serialize("Complete the trilogy") },
            new AgentMemory { Id = Guid.NewGuid().ToString(), UserId = userId, ProjectId = projectId, MemoryType = "project.constraints", Content = JsonSerializer.Serialize(new List<string> { "No violence", "PG-13" }) }
        );
        await _dbContext.SaveChangesAsync();

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
        Assert.Equal("Complete the trilogy", result.LongTermGoal);
        Assert.Equal(2, result.Constraints.Count);
        Assert.Contains("No violence", result.Constraints);
        Assert.Contains("PG-13", result.Constraints);
    }

    [Fact]
    public async Task UpdateFieldAsync_InsertsNewField_WhenNotExists()
    {
        var userId = "user123";
        var projectId = "proj456";
        var memoryType = "project.long_term_goal";
        var value = "构建修仙世界";

        await _repository.UpdateFieldAsync(userId, projectId, memoryType, value);

        var saved = await _dbContext.AgentMemories.FirstOrDefaultAsync(m =>
            m.UserId == userId && m.ProjectId == projectId && m.MemoryType == memoryType);

        Assert.NotNull(saved);
        Assert.Equal(userId, saved.UserId);
        Assert.Equal(projectId, saved.ProjectId);
        Assert.Equal(memoryType, saved.MemoryType);
        Assert.Equal(JsonSerializer.Serialize(value), saved.Content);

        _mockMemoryCache.Verify(x => x.Remove(It.IsAny<string>()), Times.Once);
        _mockRedisCache.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateMemoryAsync_UpdatesMultipleFields_InTransaction()
    {
        var userId = "user123";
        var projectId = "proj456";
        var updates = new Dictionary<string, object>
        {
            ["project.long_term_goal"] = "新目标",
            ["project.constraints"] = new List<string> { "约束1", "约束2" }
        };

        await _repository.UpdateMemoryAsync(userId, projectId, updates);

        var saved = await _dbContext.AgentMemories
            .Where(m => m.UserId == userId && m.ProjectId == projectId)
            .ToListAsync();

        Assert.Equal(2, saved.Count);
        Assert.Contains(saved, m => m.MemoryType == "project.long_term_goal");
        Assert.Contains(saved, m => m.MemoryType == "project.constraints");

        _mockMemoryCache.Verify(x => x.Remove(It.IsAny<string>()), Times.Exactly(2));
        _mockRedisCache.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}

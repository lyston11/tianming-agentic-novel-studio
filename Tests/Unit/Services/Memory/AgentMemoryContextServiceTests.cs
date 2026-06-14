using Moq;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class AgentMemoryContextServiceTests
{
    [Fact]
    public async Task BuildAsync_UsesRedisCachedContextBeforeRepositoryReads()
    {
        var cached = new AgentMemoryContextDto(
            new ChatMemoryContext("cached summary", Array.Empty<ChatHistorySummaryDto>(), Array.Empty<ChatHistoryTurnDto>()),
            new SessionMemory { CurrentGoal = "cached goal" },
            new ProjectMemory { LongTermGoal = "cached project" },
            new AuthorMemory(),
            new ExecutionMemory());
        var key = AgentMemoryKeys.MemoryContext("user-1", "session-1", "project-1", "project=1|session=2");

        var chat = new Mock<IChatHistoryRepository>(MockBehavior.Strict);
        var repo = new Mock<IAgentMemoryRepository>(MockBehavior.Strict);
        var memory = new Mock<IMemoryCacheService>();
        var redis = new Mock<IDistributedCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions
            .Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=1|session=2");
        redis
            .Setup(x => x.GetAsync<AgentMemoryContextDto>(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var service = new AgentMemoryContextService(
            chat.Object,
            repo.Object,
            redis.Object,
            memory.Object,
            versions.Object);

        var context = await service.BuildAsync("user-1", "project-1", "session-1", CancellationToken.None);

        Assert.Equal("cached summary", context.Chat.MetaSummary);
        Assert.Equal("cached goal", context.Session.CurrentGoal);
        memory.Verify(x => x.Set(key, cached, TimeSpan.FromMinutes(5)), Times.Once);
        chat.VerifyNoOtherCalls();
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BuildAsync_ComposesChatSessionProjectAuthorExecutionAndToolContext()
    {
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetPromptWindowAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatPromptWindowDto(
                "总体摘要",
                Array.Empty<ChatHistorySummaryDto>(),
                new[] { new ChatHistoryTurnDto("user", "刚上传了知识", DateTime.UtcNow) }));

        var repo = new Mock<IAgentMemoryRepository>();
        repo.Setup(x => x.GetSessionMemoryAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionMemory { CurrentGoal = "继续整理知识使用策略" });
        repo.Setup(x => x.GetProjectMemoryAsync("user-1", "project-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectMemory
            {
                ImportedKnowledgeIds = new List<string> { "knowledge-1" },
                ReferencedKnowledgeIds = new List<string>()
            });
        repo.Setup(x => x.GetAuthorMemoryAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthorMemory());
        repo.Setup(x => x.GetExecutionMemoryAsync("user-1", "project-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionMemory());

        var service = new AgentMemoryContextService(chat.Object, repo.Object);

        var context = await service.BuildAsync("user-1", "project-1", "session-1", CancellationToken.None);

        Assert.Equal("总体摘要", context.Chat.MetaSummary);
        Assert.Equal("继续整理知识使用策略", context.Session.CurrentGoal);
        Assert.Contains("knowledge-1", context.Project.ImportedKnowledgeIds);
        Assert.Empty(context.Project.ReferencedKnowledgeIds);
    }
}

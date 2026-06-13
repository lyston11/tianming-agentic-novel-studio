using Moq;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class ToolSearchCacheServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsNullWhenMemoryVersionChanged()
    {
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=2|execution=1");

        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "Planning",
            ToolSearchCacheVersion = "project=1|execution=1",
            LastToolSearchAt = DateTime.UtcNow,
            DiscoveredTools = new List<ToolSchema>
            {
                new() { Name = "PlanChapter", Description = "规划章节" }
            }
        };

        var result = await service.GetAsync(session, "Planning", CancellationToken.None);

        Assert.Null(result);
    }
}

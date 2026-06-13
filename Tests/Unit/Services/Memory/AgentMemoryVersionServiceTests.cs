using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class AgentMemoryVersionServiceTests
{
    [Fact]
    public async Task BumpAsync_IncrementsScopedVersionAndInvalidatesContextCaches()
    {
        await using var db = CreateDbContext();
        var redisCache = new Mock<IDistributedCacheService>();
        var memoryCache = new Mock<IMemoryCacheService>();
        var service = new AgentMemoryVersionService(db, redisCache.Object, memoryCache.Object);

        var first = await service.BumpAsync("user-1", "project-1", "session-1", "project");
        var second = await service.BumpAsync("user-1", "project-1", "session-1", "project");

        Assert.Equal(1, first);
        Assert.Equal(2, second);

        var saved = await db.AgentMemoryVersions.SingleAsync();
        Assert.Equal("user-1", saved.UserId);
        Assert.Equal("project-1", saved.ProjectId);
        Assert.Equal("session-1", saved.SessionId);
        Assert.Equal("project", saved.Scope);
        Assert.Equal(2, saved.Version);

        memoryCache.Verify(x => x.RemoveByPrefix("memory-context:user-1:session-1:project-1"), Times.Exactly(2));
        memoryCache.Verify(x => x.RemoveByPrefix("toolcache:user-1:session-1:project-1"), Times.Exactly(2));
        redisCache.Verify(x => x.RemoveAsync("memory-context:user-1:session-1:project-1", It.IsAny<CancellationToken>()), Times.Exactly(2));
        redisCache.Verify(x => x.RemoveAsync("toolcache:user-1:session-1:project-1", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetCombinedVersionAsync_ChangesAfterBump()
    {
        await using var db = CreateDbContext();
        var service = new AgentMemoryVersionService(
            db,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>());

        var empty = await service.GetCombinedVersionAsync("user-1", "project-1", "session-1");
        await service.BumpAsync("user-1", null, null, "author");
        await service.BumpAsync("user-1", "project-1", null, "project");
        await service.BumpAsync("user-1", "project-1", "session-1", "session");

        var combined = await service.GetCombinedVersionAsync("user-1", "project-1", "session-1");

        Assert.Equal("none=0", empty);
        Assert.Equal("author:*:*=1|project:project-1:*=1|session:project-1:session-1=1", combined);
    }

    [Fact]
    public async Task AppendAsync_PersistsMemoryEventAndBumpsScopeVersion()
    {
        await using var db = CreateDbContext();
        var versionService = new AgentMemoryVersionService(
            db,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>());
        var eventService = new AgentMemoryEventService(db, versionService);

        await eventService.AppendAsync(
            "user-1",
            "project-1",
            "session-1",
            "run-1",
            "knowledge_used",
            "tool_call",
            "project",
            "project.referenced_knowledge_ids",
            new { knowledgeId = "knowledge-1" });

        var savedEvent = await db.AgentMemoryEvents.SingleAsync();
        Assert.Equal("user-1", savedEvent.UserId);
        Assert.Equal("project-1", savedEvent.ProjectId);
        Assert.Equal("session-1", savedEvent.SessionId);
        Assert.Equal("run-1", savedEvent.RunId);
        Assert.Equal("knowledge_used", savedEvent.SourceType);
        Assert.Equal("tool_call", savedEvent.TriggerType);
        Assert.Equal("project", savedEvent.MemoryScope);
        Assert.Equal("project.referenced_knowledge_ids", savedEvent.MemoryKey);
        Assert.Contains("\"knowledgeId\":\"knowledge-1\"", savedEvent.PayloadJson);
        Assert.False(string.IsNullOrWhiteSpace(savedEvent.Id));

        var savedVersion = await db.AgentMemoryVersions.SingleAsync();
        Assert.Equal("project", savedVersion.Scope);
        Assert.Equal(1, savedVersion.Version);
    }

    [Fact]
    public void MemoryCacheService_RemoveByPrefix_RemovesFullPrefixMatches()
    {
        using var innerCache = new MemoryCache(new MemoryCacheOptions());
        var service = new MemoryCacheService(innerCache, NullLogger<MemoryCacheService>.Instance);

        service.Set("memory-context:u:s:p:v1", "old-context", TimeSpan.FromMinutes(5));
        service.Set("memory-context:u:s:p2:v1", "sibling-context", TimeSpan.FromMinutes(5));
        service.Set("memory-context:u:s:other:v1", "other-context", TimeSpan.FromMinutes(5));
        service.Set("toolcache:u:s:p:phase:v1", "old-tool", TimeSpan.FromMinutes(5));

        service.RemoveByPrefix("memory-context:u:s:p");

        Assert.Null(service.Get<string>("memory-context:u:s:p:v1"));
        Assert.Equal("sibling-context", service.Get<string>("memory-context:u:s:p2:v1"));
        Assert.Equal("other-context", service.Get<string>("memory-context:u:s:other:v1"));
        Assert.Equal("old-tool", service.Get<string>("toolcache:u:s:p:phase:v1"));
    }

    private static NovelAgentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new NovelAgentDbContext(options);
    }
}

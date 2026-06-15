using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public class ProjectKnowledgeUsageServiceTests
{
    [Fact]
    public async Task MarkReferencedAsync_DoesNotPolluteOtherProjects()
    {
        await using var db = CreateDb();
        Seed(db);
        var events = new Mock<IAgentMemoryEventService>();
        var service = new ProjectKnowledgeUsageService(db, events.Object, NullLogger<ProjectKnowledgeUsageService>.Instance);

        await service.MarkImportedAsync("user-1", "project-b", "knowledge-1", "session-b", "upload", CancellationToken.None);
        await service.MarkReferencedAsync("user-1", "project-a", "knowledge-1", "session-a", "run-a", CancellationToken.None);

        var a = await db.ProjectKnowledgeUsages.SingleAsync(x => x.ProjectId == "project-a");
        var b = await db.ProjectKnowledgeUsages.SingleAsync(x => x.ProjectId == "project-b");

        Assert.Equal("referenced", a.Status);
        Assert.Equal(1, a.UsageCount);
        Assert.Equal("imported", b.Status);
        Assert.Equal(0, b.UsageCount);
    }

    [Fact]
    public async Task MarkReferencedAsync_InvalidatesOnlyCurrentProjectKnowledgeAndMemoryCaches()
    {
        await using var db = CreateDb();
        Seed(db);
        var events = new Mock<IAgentMemoryEventService>();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        redis.Setup(x => x.RemoveByPrefixAsync("knowledge:search:user-1:project-a", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        redis.Setup(x => x.RemoveAsync("knowledge:inventory:user-1:project-a", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        redis.Setup(x => x.RemoveAsync("memory:project:user-1:project-a", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        redis.Setup(x => x.RemoveAsync("memory:session:user-1:session-a", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new ProjectKnowledgeUsageService(
            db,
            events.Object,
            null,
            NullLogger<ProjectKnowledgeUsageService>.Instance,
            redis.Object,
            memory.Object);

        await service.MarkReferencedAsync("user-1", "project-a", "knowledge-1", "session-a", "run-a", CancellationToken.None);

        memory.Verify(x => x.RemoveByPrefix("knowledge:search:user-1:project-a"), Times.Once);
        memory.Verify(x => x.Remove("knowledge:inventory:user-1:project-a"), Times.Once);
        memory.Verify(x => x.Remove("memory:project:user-1:project-a"), Times.Once);
        memory.Verify(x => x.Remove("memory:session:user-1:session-a"), Times.Once);
        redis.Verify(x => x.RemoveByPrefixAsync("knowledge:search:user-1:project-b", It.IsAny<CancellationToken>()), Times.Never);
        memory.Verify(x => x.RemoveByPrefix("knowledge:search:user-1:project-b"), Times.Never);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void Seed(NovelAgentDbContext db)
    {
        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.NovelProjects.Add(new NovelProject { Id = "project-a", UserId = "user-1", Title = "A" });
        db.NovelProjects.Add(new NovelProject { Id = "project-b", UserId = "user-1", Title = "B" });
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-a",
            EntryType = "ReaderPromise",
            Title = "代价",
            Content = "胜利要有代价"
        });
        db.SaveChanges();
    }
}

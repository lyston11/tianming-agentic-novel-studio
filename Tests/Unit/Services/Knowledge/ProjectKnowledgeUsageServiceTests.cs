using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
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
            ProjectId = "project-a",
            EntryType = "ReaderPromise",
            Title = "代价",
            Content = "胜利要有代价"
        });
        db.SaveChanges();
    }
}

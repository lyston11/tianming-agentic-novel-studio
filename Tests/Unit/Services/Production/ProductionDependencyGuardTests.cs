using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionDependencyGuardTests
{
    [Fact]
    public async Task FindBlocksAsync_BlocksNextChapterWhenPreviousPostCommitOutboxIsPending()
    {
        await using var db = CreateDb();
        SeedProjectWithChapters(db);
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id = "outbox-finalize-001",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-001",
            EventType = "finalize_chapter_commit_metadata",
            AggregateType = "chapter",
            AggregateId = "project-1-chapter-001",
            Status = "pending",
            PayloadJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionDependencyGuard guard = new ProductionDependencyGuard(db);

        var blocks = await guard.FindBlocksAsync(new ProductionDependencyGuardRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            TargetChapterNumber: 2));

        var block = Assert.Single(blocks);
        Assert.Equal("previous_chapter_post_commit_outbox_pending", block.Code);
        Assert.Equal(2, block.TargetChapterNumber);
        Assert.Equal("project-1-chapter-001", block.PreviousChapterId);
        Assert.Equal(1, block.PreviousChapterNumber);
        Assert.Contains("outbox-finalize-001", block.OutboxEventIds);
        Assert.Contains("finalize_chapter_commit_metadata", block.OutboxEventTypes);
        Assert.Contains("pending", block.Statuses);
        Assert.Equal("QueryNovelProductionState", block.RecommendedToolName);
    }

    [Fact]
    public async Task FindBlocksAsync_BlocksNextChapterWhenPreviousChapterIndexOutboxIsPending()
    {
        await using var db = CreateDb();
        SeedProjectWithChapters(db);
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id = "outbox-index-001",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-001",
            EventType = "index_chapter_content",
            AggregateType = "chapter_version",
            AggregateId = "version-chapter-001",
            Status = "pending",
            PayloadJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.ChapterVersions.Add(new ChapterVersion
        {
            Id = "version-chapter-001",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "project-1-chapter-001",
            ContentDocumentId = "doc-chapter-001",
            VersionNumber = 1,
            Title = "第一章",
            WordCount = 1200,
            Status = "committed",
            RuntimeRunId = "run-001",
            PackageId = "pkg-001",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionDependencyGuard guard = new ProductionDependencyGuard(db);

        var blocks = await guard.FindBlocksAsync(new ProductionDependencyGuardRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            TargetChapterNumber: 2));

        var block = Assert.Single(blocks);
        Assert.Equal("previous_chapter_post_commit_outbox_pending", block.Code);
        Assert.Equal("project-1-chapter-001", block.PreviousChapterId);
        Assert.Contains("outbox-index-001", block.OutboxEventIds);
        Assert.Contains("index_chapter_content", block.OutboxEventTypes);
        Assert.Contains("pending", block.Statuses);
    }

    [Fact]
    public async Task FindBlocksAsync_DoesNotBlockWhenPreviousPostCommitOutboxCompleted()
    {
        await using var db = CreateDb();
        SeedProjectWithChapters(db);
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id = "outbox-finalize-001",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-001",
            EventType = "finalize_chapter_commit_metadata",
            AggregateType = "chapter",
            AggregateId = "project-1-chapter-001",
            Status = "completed",
            PayloadJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionDependencyGuard guard = new ProductionDependencyGuard(db);

        var blocks = await guard.FindBlocksAsync(new ProductionDependencyGuardRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            TargetChapterNumber: 2));

        Assert.Empty(blocks);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectWithChapters(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "依赖守卫测试书",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.AddRange(
            new Chapter
            {
                Id = "project-1-chapter-001",
                ProjectId = "project-1",
                Title = "第一章",
                ChapterNumber = 1,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Chapter
            {
                Id = "project-1-chapter-002",
                ProjectId = "project-1",
                Title = "第二章",
                ChapterNumber = 2,
                Status = "planned",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionChapterSummaryServiceTests
{
    [Fact]
    public void Constructor_FailsFastWhenProjectHasNoDatabaseScope()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProductionChapterSummaryService((IServiceScopeFactory)null!, "user-1", "project-1"));

        Assert.Equal("scopeFactory", exception.ParamName);
    }

    [Fact]
    public async Task GetPreviousSummariesAsync_ReadsCommittedSummariesFromProductionEvents()
    {
        await using var db = CreateDb();
        SeedProjectsAndChapters(db);
        var writer = new ProductionEventWriter(new ProductionTruthStore(db));
        var summaries = new ProductionChapterSummaryService(db, "user-1", "project-1");

        await summaries.SetSummaryAsync("vol1_ch001", "第一章：林澈拿到银蓝邮徽。");
        await summaries.SetSummaryAsync("vol1_ch002", "第二章：林澈沿旧邮路逃离雨站。");
        await summaries.SetSummaryAsync("vol1_ch004", "第四章：未来摘要不能提前泄露。");
        await writer.AppendChapterStageAsync(new AppendChapterProductionEventRequest(
            RuntimeRunId: "other-run",
            UserId: "user-2",
            ProjectId: "project-2",
            ChapterId: "vol1_ch001-other",
            PackageId: null,
            EventType: "chapter_summary_recorded",
            Stage: "chapter_commit",
            Status: "completed",
            Message: "其他项目摘要",
            ArtifactType: "chapter_summary",
            ArtifactId: "vol1_ch001-other:summary",
            Data: new { chapterId = "vol1_ch001-other", summary = "其他项目不能混入。" }));

        var result = await summaries.GetPreviousSummariesAsync("vol1_ch003", 2);

        Assert.Equal(new[] { "vol1_ch001", "vol1_ch002" }, result.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("第一章：林澈拿到银蓝邮徽。", result["vol1_ch001"]);
        Assert.Equal("第二章：林澈沿旧邮路逃离雨站。", result["vol1_ch002"]);
        Assert.DoesNotContain(result, pair => pair.Value.Contains("未来摘要", StringComparison.Ordinal));
        Assert.DoesNotContain(result, pair => pair.Value.Contains("其他项目", StringComparison.Ordinal));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectsAndChapters(NovelAgentDbContext db)
    {
        db.Users.AddRange(
            new User
            {
                Id = "user-1",
                Username = "user1",
                Email = "user1@example.com",
                PasswordHash = "hash",
                Role = "User",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            },
            new User
            {
                Id = "user-2",
                Username = "user2",
                Email = "user2@example.com",
                PasswordHash = "hash",
                Role = "User",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        db.NovelProjects.AddRange(
            new NovelProject
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "旧邮路",
                Genre = "末世",
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new NovelProject
            {
                Id = "project-2",
                UserId = "user-2",
                Title = "别的书",
                Genre = "末世",
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        db.Chapters.AddRange(
            NewChapter("vol1_ch001", "project-1", 1),
            NewChapter("vol1_ch002", "project-1", 2),
            NewChapter("vol1_ch004", "project-1", 4),
            NewChapter("vol1_ch001-other", "project-2", 1));
        db.SaveChanges();
    }

    private static Chapter NewChapter(string id, string projectId, int number) => new()
    {
        Id = id,
        ProjectId = projectId,
        Title = $"第{number}章",
        ChapterNumber = number,
        Status = "committed",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

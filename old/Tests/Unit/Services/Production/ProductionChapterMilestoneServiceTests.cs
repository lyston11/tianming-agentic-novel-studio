using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionChapterMilestoneServiceTests
{
    [Fact]
    public async Task GetPreviousMilestonesAsync_BuildsVolumeMilestonesFromProductionSummaries()
    {
        await using var db = CreateDb();
        SeedProjectsAndChapters(db);
        var summaries = new ProductionChapterSummaryService(db, "user-1", "project-1");
        var milestones = new ProductionChapterMilestoneService(summaries);

        await summaries.SetSummaryAsync("vol1_ch001", "第一卷第一章：林澈发现旧邮路。");
        await summaries.SetSummaryAsync("vol1_ch002", "第一卷第二章：邮徽发出银蓝骨光。");
        await summaries.SetSummaryAsync("vol2_ch001", "第二卷第一章：林澈抵达灰塔驿站。");
        await summaries.SetSummaryAsync("vol3_ch001", "第三卷当前卷不能提前注入。");
        await new ProductionChapterSummaryService(db, "user-2", "project-2")
            .SetSummaryAsync("vol1_ch001-other", "其他项目的旧邮路不能混入。");

        var result = await milestones.GetPreviousMilestonesAsync(3);

        Assert.Equal(new[] { 1, 2 }, result.Select(item => item.VolumeNumber).ToArray());
        Assert.Contains("第一卷第一章：林澈发现旧邮路。", result[0].Milestone);
        Assert.Contains("第一卷第二章：邮徽发出银蓝骨光。", result[0].Milestone);
        Assert.Contains("第二卷第一章：林澈抵达灰塔驿站。", result[1].Milestone);
        Assert.DoesNotContain("第三卷当前卷", string.Join("\n", result.Select(item => item.Milestone)), StringComparison.Ordinal);
        Assert.DoesNotContain("其他项目", string.Join("\n", result.Select(item => item.Milestone)), StringComparison.Ordinal);
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
            NewChapter("vol2_ch001", "project-1", 1),
            NewChapter("vol3_ch001", "project-1", 1),
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

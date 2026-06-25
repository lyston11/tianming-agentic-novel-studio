using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionVolumeFactArchiveServiceTests
{
    [Fact]
    public void Constructor_FailsFastWhenProjectHasNoDatabaseScope()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProductionVolumeFactArchiveService((IServiceScopeFactory)null!, "user-1", "project-1"));

        Assert.Equal("scopeFactory", exception.ParamName);
    }

    [Fact]
    public async Task GetPreviousArchivesAsync_ReadsLatestVolumeSnapshotsFromProductionTruth()
    {
        await using var db = CreateDb();
        SeedProjectsChaptersAndSnapshots(db);

        var archiveService = new ProductionVolumeFactArchiveService(db, "user-1", "project-1");

        var result = await archiveService.GetPreviousArchivesAsync(3);

        Assert.Equal(new[] { 1, 2 }, result.Select(item => item.VolumeNumber).ToArray());
        Assert.Equal("vol1_ch002", result[0].LastChapterId);
        Assert.Equal("林澈", Assert.Single(result[0].CharacterStates).Name);
        Assert.Equal("银蓝邮徽已认主", result[0].CharacterStates[0].Stage);
        Assert.Equal("灰塔驿站冲突", Assert.Single(result[1].ConflictProgress).Name);
        Assert.DoesNotContain(result, archive => archive.LastChapterId == "vol3_ch001");
        Assert.DoesNotContain(result, archive => archive.CharacterStates.Any(state => state.Name == "沈烁"));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectsChaptersAndSnapshots(NovelAgentDbContext db)
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
            NewProject("project-1", "user-1", "旧邮路"),
            NewProject("project-2", "user-2", "别的书"));
        db.Chapters.AddRange(
            NewChapter("vol1_ch001", "project-1", 1),
            NewChapter("vol1_ch002", "project-1", 2),
            NewChapter("vol2_ch001", "project-1", 1),
            NewChapter("vol3_ch001", "project-1", 1),
            NewChapter("vol1_ch001-other", "project-2", 1));
        db.ProjectFactSnapshots.AddRange(
            NewSnapshot("snap-older", "user-1", "project-1", "vol1_ch001", 1, """{"CharacterStates":[{"Id":"lin-che","Name":"林澈","Stage":"尚未认主","ChapterId":"vol1_ch001"}]}""", -5),
            NewSnapshot("snap-v1", "user-1", "project-1", "vol1_ch002", 2, """{"CharacterStates":[{"Id":"lin-che","Name":"林澈","Stage":"银蓝邮徽已认主","Abilities":"识别旧邮路","Relationships":"独自行动","ChapterId":"vol1_ch002"}]}""", -4),
            NewSnapshot("snap-v2", "user-1", "project-1", "vol2_ch001", 1, """{"ConflictProgress":[{"Id":"gray-tower","Name":"灰塔驿站冲突","Status":"升级","RecentProgress":["灰塔守卫封站"]}]}""", -3),
            NewSnapshot("snap-current", "user-1", "project-1", "vol3_ch001", 1, """{"CharacterStates":[{"Id":"lin-che","Name":"林澈","Stage":"第三卷状态","ChapterId":"vol3_ch001"}]}""", -2),
            NewSnapshot("snap-other", "user-2", "project-2", "vol1_ch001-other", 1, """{"CharacterStates":[{"Id":"shen-shuo","Name":"沈烁","Stage":"其他项目","ChapterId":"vol1_ch001-other"}]}""", -1));
        db.SaveChanges();
    }

    private static NovelProject NewProject(string id, string userId, string title) => new()
    {
        Id = id,
        UserId = userId,
        Title = title,
        Genre = "末世",
        Status = "draft",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

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

    private static ProjectFactSnapshot NewSnapshot(
        string id,
        string userId,
        string projectId,
        string chapterId,
        int version,
        string snapshotJson,
        int minutesOffset) => new()
    {
        Id = id,
        UserId = userId,
        ProjectId = projectId,
        ChapterId = chapterId,
        VersionNumber = version,
        SnapshotJson = snapshotJson,
        Source = "chapter_commit",
        CreatedAt = DateTime.UtcNow.AddMinutes(minutesOffset)
    };
}

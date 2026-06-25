using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Workspace;
using Xunit;

namespace Tests.Unit.Services.Workspace;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public async Task GetWorkspaceAsync_CountsCanonicalVolumesWhenVolumeArcsAreAbsent()
    {
        await using var db = CreateDb();
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
            Title = "书城正式卷测试",
            Genre = "废土",
            Status = "writing",
            UpdatedAt = DateTime.UtcNow
        });
        db.Volumes.Add(new Volume
        {
            Id = "volume-1",
            ProjectId = "project-1",
            Title = "第一卷：黑雨旧邮路",
            VolumeNumber = 1
        });
        db.Chapters.AddRange(
            new Chapter
            {
                Id = "chapter-1",
                ProjectId = "project-1",
                VolumeId = "volume-1",
                Title = "第一章：邮徽醒来",
                ChapterNumber = 1,
                Status = "committed",
                WordCount = 3200
            },
            new Chapter
            {
                Id = "chapter-2",
                ProjectId = "project-1",
                VolumeId = "volume-1",
                Title = "第二章：旧站台追击",
                ChapterNumber = 2,
                Status = "committed",
                WordCount = 3500
            });
        await db.SaveChangesAsync();

        var service = new WorkspaceService(db);

        var workspace = await service.GetWorkspaceAsync("user-1");

        var book = Assert.Single(workspace.Projects);
        Assert.Equal("project-1", book.ProjectId);
        Assert.Equal(1, book.VolumeCount);
        Assert.Equal(2, book.GeneratedChapterCount);
        Assert.Equal(0, book.PlannedChapterCount);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}

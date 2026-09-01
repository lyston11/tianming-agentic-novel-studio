using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionDesignElementLookupServiceTests
{
    [Fact]
    public void Constructor_FailsFastWhenProjectHasNoDatabaseScope()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProductionDesignElementLookupService((IServiceScopeFactory)null!, "user-1", "project-1"));

        Assert.Equal("scopeFactory", exception.ParamName);
    }

    [Fact]
    public async Task ResolvesNamesFromCurrentProjectDatabaseAndFactSnapshots()
    {
        await using var db = CreateDb();
        SeedProjectData(db);

        var lookup = new ProductionDesignElementLookupService(db, "user-1", "project-1");

        Assert.Equal("林澈", await lookup.ResolveCharacterNameAsync("char-lin"));
        Assert.Equal("灰塔驿站", await lookup.ResolveLocationNameAsync("loc-gray-tower"));
        Assert.Equal("邮差工会", await lookup.ResolveFactionNameAsync("fac-post"));
        Assert.Equal("灰塔封站", await lookup.ResolveConflictNameAsync("conf-gray"));
        Assert.Null(await lookup.ResolveCharacterNameAsync("char-other"));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectData(NovelAgentDbContext db)
    {
        db.Users.AddRange(
            NewUser("user-1", "user1"),
            NewUser("user-2", "user2"));
        db.NovelProjects.AddRange(
            NewProject("project-1", "user-1", "旧邮路"),
            NewProject("project-2", "user-2", "别的书"));
        db.Characters.AddRange(
            new Character
            {
                Id = "char-lin",
                UserId = "user-1",
                ProjectId = "project-1",
                Name = "林澈",
                Role = "protagonist",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Character
            {
                Id = "char-other",
                UserId = "user-2",
                ProjectId = "project-2",
                Name = "沈烁",
                Role = "protagonist",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "snapshot-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "vol1_ch002",
            VersionNumber = 1,
            Source = "chapter_commit",
            SnapshotJson = """
            {
              "LocationStates":[{"Id":"loc-gray-tower","Name":"灰塔驿站","Status":"封站"}],
              "FactionStates":[{"Id":"fac-post","Name":"邮差工会","Status":"追捕林澈"}],
              "ConflictProgress":[{"Id":"conf-gray","Name":"灰塔封站","Status":"升级"}]
            }
            """,
            CreatedAt = DateTime.UtcNow
        });
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "snapshot-other",
            UserId = "user-2",
            ProjectId = "project-2",
            ChapterId = "vol1_ch002",
            VersionNumber = 1,
            Source = "chapter_commit",
            SnapshotJson = """
            {
              "LocationStates":[{"Id":"loc-gray-tower","Name":"其他项目地点"}],
              "FactionStates":[{"Id":"fac-post","Name":"其他项目势力"}],
              "ConflictProgress":[{"Id":"conf-gray","Name":"其他项目冲突"}]
            }
            """,
            CreatedAt = DateTime.UtcNow.AddMinutes(1)
        });
        db.SaveChanges();
    }

    private static User NewUser(string id, string username) => new()
    {
        Id = id,
        Username = username,
        Email = $"{username}@example.com",
        PasswordHash = "hash",
        Role = "User",
        CreatedAt = DateTime.UtcNow,
        IsActive = true
    };

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
}

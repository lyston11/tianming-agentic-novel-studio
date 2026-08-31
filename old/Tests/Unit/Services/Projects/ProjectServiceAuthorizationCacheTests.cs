using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Projects;
using Xunit;

namespace Tests.Unit.Services.Projects;

public sealed class ProjectServiceAuthorizationCacheTests
{
    [Fact]
    public async Task CreateProjectAsync_WithSameIdempotencyKey_ReturnsExistingProject()
    {
        await using var db = CreateDb();
        SeedUser(db, "user-1");
        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheService>.Instance);
        var service = new ProjectService(
            db,
            NullLogger<ProjectService>.Instance,
            cache,
            new ProductionTruthStore(db));
        var request = new TM.Web.NovelAgentWeb.Models.Projects.CreateProjectRequest
        {
            Title = "雾城邮路",
            Genre = "末世",
            CoreHook = "旧邮路在怪物围城中重启。",
            IdempotencyKey = "project-key-001"
        };

        var first = await service.CreateProjectAsync(request, "user-1");
        var second = await service.CreateProjectAsync(request, "user-1");

        Assert.Equal(first.Id, second.Id);
        var project = await db.NovelProjects.SingleAsync();
        Assert.Equal("project-key-001", project.IdempotencyKey);
    }

    [Fact]
    public async Task GetProjectByIdAsync_DoesNotServeOwnerCacheEntryToAnotherUser()
    {
        await using var db = CreateDb();
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "owner-user",
            Title = "Owner Project",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheService>.Instance);
        var service = new ProjectService(
            db,
            NullLogger<ProjectService>.Instance,
            cache,
            new ProductionTruthStore(db));

        var ownerProject = await service.GetProjectByIdAsync("project-1", "owner-user", isAdmin: false);
        Assert.Equal("Owner Project", ownerProject.Title);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetProjectByIdAsync("project-1", "other-user", isAdmin: false));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedUser(NovelAgentDbContext db, string userId)
    {
        db.Users.Add(new User
        {
            Id = userId,
            Username = userId,
            Email = $"{userId}@example.test",
            PasswordHash = "hash",
            Role = "User",
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }
}

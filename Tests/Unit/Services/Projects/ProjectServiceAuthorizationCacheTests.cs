using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Projects;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Projects;

public sealed class ProjectServiceAuthorizationCacheTests
{
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
            Mock.Of<IVectorStore>(),
            NullLogger<ProjectService>.Instance,
            cache);

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
}

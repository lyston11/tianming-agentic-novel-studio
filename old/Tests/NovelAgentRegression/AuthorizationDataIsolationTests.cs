using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Extensions;
using TM.Web.NovelAgentWeb.Models.Auth;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

/// <summary>
/// Integration tests for authorization middleware and data isolation.
/// Verifies that users can only access their own data and admins can access all data.
/// </summary>
public class AuthorizationDataIsolationTests : IAsyncLifetime
{
    private NovelAgentDbContext _dbContext = null!;
    private string _userAId = null!;
    private string _userBId = null!;
    private string _adminId = null!;

    public async Task InitializeAsync()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new NovelAgentDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        // Create test users
        var userA = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user_a",
            Email = "user_a@test.com",
            PasswordHash = "hash_a",
            Role = "author",
            IsActive = true
        };

        var userB = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user_b",
            Email = "user_b@test.com",
            PasswordHash = "hash_b",
            Role = "author",
            IsActive = true
        };

        var admin = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "admin",
            Email = "admin@test.com",
            PasswordHash = "hash_admin",
            Role = "admin",
            IsActive = true
        };

        _dbContext.Users.AddRange(userA, userB, admin);
        await _dbContext.SaveChangesAsync();

        _userAId = userA.Id;
        _userBId = userB.Id;
        _adminId = admin.Id;

        // Create test projects for each user
        var projectA1 = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = _userAId,
            Title = "User A Project 1",
            Genre = "Fantasy",
            CreatedAt = DateTime.UtcNow
        };

        var projectA2 = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = _userAId,
            Title = "User A Project 2",
            Genre = "SciFi",
            CreatedAt = DateTime.UtcNow
        };

        var projectB1 = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = _userBId,
            Title = "User B Project 1",
            Genre = "Romance",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.NovelProjects.AddRange(projectA1, projectA2, projectB1);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    [Fact]
    public void QueryFilterExtensions_WithUserFilter_FiltersCorrectly()
    {
        // Arrange
        var query = _dbContext.NovelProjects.AsQueryable();

        // Act
        var userAProjects = query.WithUserFilter(_userAId).ToList();
        var userBProjects = query.WithUserFilter(_userBId).ToList();

        // Assert
        Assert.Equal(2, userAProjects.Count);
        Assert.All(userAProjects, p => Assert.Equal(_userAId, p.UserId));

        Assert.Single(userBProjects);
        Assert.All(userBProjects, p => Assert.Equal(_userBId, p.UserId));
    }

    [Fact]
    public void QueryFilterExtensions_WithUserFilterIfNotAdmin_AdminBypass()
    {
        // Arrange
        var query = _dbContext.NovelProjects.AsQueryable();

        // Act
        var regularUserProjects = query.WithUserFilterIfNotAdmin(_userAId, isAdmin: false).ToList();
        var adminProjects = query.WithUserFilterIfNotAdmin(_adminId, isAdmin: true).ToList();

        // Assert
        Assert.Equal(2, regularUserProjects.Count); // Only user A's projects
        Assert.Equal(3, adminProjects.Count); // All projects (admin bypass)
    }

    [Fact]
    public void QueryFilterExtensions_WithUserFilter_ThrowsOnNullUserId()
    {
        // Arrange
        var query = _dbContext.NovelProjects.AsQueryable();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => query.WithUserFilter(null!).ToList());
        Assert.Throws<ArgumentNullException>(() => query.WithUserFilter(string.Empty).ToList());
    }

    [Fact]
    public void QueryFilterExtensions_WithUserFilter_ThrowsOnInvalidEntityType()
    {
        // Arrange
        var query = _dbContext.Users.AsQueryable(); // User entity doesn't have UserId property

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            query.WithUserFilter(_userAId).ToList());
        Assert.Contains("does not have a string UserId property", exception.Message);
    }

    [Fact]
    public void QueryFilterExtensions_WhereUserOwns_FiltersEnumerable()
    {
        // Arrange
        var allProjects = _dbContext.NovelProjects.ToList();

        // Act
        var userAProjects = allProjects.WhereUserOwns(_userAId).ToList();
        var userBProjects = allProjects.WhereUserOwns(_userBId).ToList();

        // Assert
        Assert.Equal(2, userAProjects.Count);
        Assert.All(userAProjects, p => Assert.Equal(_userAId, p.UserId));

        Assert.Single(userBProjects);
        Assert.All(userBProjects, p => Assert.Equal(_userBId, p.UserId));
    }

    [Fact]
    public async Task DataIsolation_UserCannotAccessOtherUsersProjects()
    {
        // Arrange - Get User A's project
        var userAProject = await _dbContext.NovelProjects
            .FirstAsync(p => p.UserId == _userAId);

        // Act - Try to access with User B's filter
        var userBAccessAttempt = await _dbContext.NovelProjects
            .WithUserFilter(_userBId)
            .FirstOrDefaultAsync(p => p.Id == userAProject.Id);

        // Assert - User B cannot see User A's project
        Assert.Null(userBAccessAttempt);
    }

    [Fact]
    public async Task DataIsolation_AdminCanAccessAllProjects()
    {
        // Arrange
        var allProjects = await _dbContext.NovelProjects.ToListAsync();

        // Act - Admin queries with bypass
        var adminAccessibleProjects = await _dbContext.NovelProjects
            .WithUserFilterIfNotAdmin(_adminId, isAdmin: true)
            .ToListAsync();

        // Assert - Admin can see all projects
        Assert.Equal(allProjects.Count, adminAccessibleProjects.Count);
    }

    [Fact]
    public async Task DataIsolation_MultipleUsersCannotSeeEachOthersData()
    {
        // Act
        var userAView = await _dbContext.NovelProjects
            .WithUserFilter(_userAId)
            .Select(p => new { p.Id, p.Title, p.UserId })
            .ToListAsync();

        var userBView = await _dbContext.NovelProjects
            .WithUserFilter(_userBId)
            .Select(p => new { p.Id, p.Title, p.UserId })
            .ToListAsync();

        // Assert - No overlap in visible projects
        var userAProjectIds = userAView.Select(p => p.Id).ToHashSet();
        var userBProjectIds = userBView.Select(p => p.Id).ToHashSet();

        Assert.Empty(userAProjectIds.Intersect(userBProjectIds));
    }

    [Fact]
    public async Task DataIsolation_ComplexQueryWithUserFilter()
    {
        // Arrange - Add materials for users
        var materialA = new Material
        {
            Id = Guid.NewGuid().ToString(),
            UserId = _userAId,
            Title = "Material A",
            ContentType = "text/plain",
            CreatedAt = DateTime.UtcNow
        };

        var materialB = new Material
        {
            Id = Guid.NewGuid().ToString(),
            UserId = _userBId,
            Title = "Material B",
            ContentType = "text/plain",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Materials.AddRange(materialA, materialB);
        await _dbContext.SaveChangesAsync();

        // Act - Complex query with joins and filters
        var userAData = await (from p in _dbContext.NovelProjects.WithUserFilter(_userAId)
                               join u in _dbContext.Users on p.UserId equals u.Id
                               select new { p.Title, u.Username })
                              .ToListAsync();

        var userAMaterials = await _dbContext.Materials
            .WithUserFilter(_userAId)
            .ToListAsync();

        // Assert
        Assert.Equal(2, userAData.Count);
        Assert.All(userAData, d => Assert.Equal("user_a", d.Username));

        Assert.Single(userAMaterials);
        Assert.Equal("Material A", userAMaterials[0].Title);
    }

    [Fact]
    public async Task DataIsolation_CountOperationsRespectUserFilter()
    {
        // Act
        var userAProjectCount = await _dbContext.NovelProjects
            .WithUserFilter(_userAId)
            .CountAsync();

        var userBProjectCount = await _dbContext.NovelProjects
            .WithUserFilter(_userBId)
            .CountAsync();

        var adminProjectCount = await _dbContext.NovelProjects
            .WithUserFilterIfNotAdmin(_adminId, isAdmin: true)
            .CountAsync();

        // Assert
        Assert.Equal(2, userAProjectCount);
        Assert.Equal(1, userBProjectCount);
        Assert.Equal(3, adminProjectCount); // All projects
    }
}

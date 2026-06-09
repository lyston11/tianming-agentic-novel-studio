using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Support;
using TM.Tests.NovelAgentRegression.Helpers;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

/// <summary>
/// Unit tests for WorkspaceFactory.
/// Tests cache behavior, LRU eviction, reference counting, and thread safety.
/// </summary>
public class WorkspaceFactoryTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly NovelAgentDbContext _dbContext;
    private readonly TestDbContext _testDb;

    public WorkspaceFactoryTests()
    {
        // Create in-memory database
        _testDb = TestDbContextFactory.CreateDisposableContext();
        _dbContext = _testDb.Context;

        // Seed test user and project
        var user = new User
        {
            Id = "test-user-1",
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hash",
            Role = "author",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);

        var project = new NovelProject
        {
            Id = "test-project-1",
            UserId = "test-user-1",
            Title = "Test Project",
            Genre = "Fantasy",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.NovelProjects.Add(project);
        _dbContext.SaveChanges();

        // Configure service provider
        var services = new ServiceCollection();

        // Add DbContext
        services.AddSingleton(_dbContext);

        // Add configuration
        var configData = new Dictionary<string, string>
        {
            { "NovelAgent:ProjectName", "TestProject" },
            { "NovelAgent:StorageRoot", Path.Combine(Path.GetTempPath(), "WorkspaceFactoryTests") }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Add hosting environment mock
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());
        services.AddSingleton(envMock.Object);

        // Add UserSettingsManager mock
        var settingsManagerMock = new Mock<UserSettingsManager>();
        services.AddSingleton(settingsManagerMock.Object);

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        _testDb?.Dispose();
    }

    [Fact]
    public void Constructor_InitializesSuccessfully()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions
        {
            MaxCachedWorkspaces = 10,
            IdleTimeoutMinutes = 5
        });

        // Act
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Assert
        Assert.NotNull(factory);
        var stats = factory.GetStats();
        Assert.Equal(0, stats.TotalWorkspaces);
        Assert.Equal(0, stats.CacheHits);
        Assert.Equal(0, stats.CacheMisses);
    }

    [Fact]
    public async Task AcquireAsync_FirstCall_CreatesCacheEntry()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Act
        var entry = await factory.AcquireAsync("test-user-1", "test-project-1");

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("test-user-1", entry.UserId);
        Assert.Equal("test-project-1", entry.ProjectId);
        Assert.NotNull(entry.Workspace);
        Assert.Equal(1, entry.ActiveReferences);

        var stats = factory.GetStats();
        Assert.Equal(1, stats.TotalWorkspaces);
        Assert.Equal(0, stats.CacheHits);
        Assert.Equal(1, stats.CacheMisses);
    }

    [Fact]
    public async Task AcquireAsync_SecondCall_ReturnsCachedEntry()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Act
        var entry1 = await factory.AcquireAsync("test-user-1", "test-project-1");
        var entry2 = await factory.AcquireAsync("test-user-1", "test-project-1");

        // Assert
        Assert.Same(entry1, entry2);
        Assert.Equal(2, entry1.ActiveReferences);

        var stats = factory.GetStats();
        Assert.Equal(1, stats.TotalWorkspaces);
        Assert.Equal(1, stats.CacheHits);
        Assert.Equal(1, stats.CacheMisses);
        Assert.Equal(0.5, stats.CacheHitRate);
    }

    [Fact]
    public async Task Release_DecrementsReferenceCount()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);
        var entry = await factory.AcquireAsync("test-user-1", "test-project-1");
        Assert.Equal(1, entry.ActiveReferences);

        // Act
        factory.Release("test-user-1", "test-project-1");

        // Assert
        Assert.Equal(0, entry.ActiveReferences);

        var stats = factory.GetStats();
        Assert.Equal(1, stats.IdleWorkspaces);
    }

    [Fact]
    public async Task Touch_UpdatesLastAccessTime()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);
        var entry = await factory.AcquireAsync("test-user-1", "test-project-1");
        factory.Release("test-user-1", "test-project-1");

        var lastAccessBefore = entry.LastAccessTime;
        await Task.Delay(100); // Small delay to ensure time difference

        // Act
        factory.Touch("test-user-1", "test-project-1");

        // Assert
        Assert.True(entry.LastAccessTime > lastAccessBefore);
    }

    [Fact]
    public void GetStats_ReturnsAccurateStatistics()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Act
        var stats = factory.GetStats();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.TotalWorkspaces);
        Assert.Equal(0, stats.ActiveReferences);
        Assert.Equal(0, stats.IdleWorkspaces);
        Assert.Equal(0, stats.TotalEvictions);
        Assert.Equal(0.0, stats.CacheHitRate);
    }

    [Fact]
    public async Task AcquireAsync_CacheMiss_IncrementsCounter()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Act
        await factory.AcquireAsync("test-user-1", "test-project-1");

        // Assert
        var stats = factory.GetStats();
        Assert.Equal(1, stats.CacheMisses);
        Assert.Equal(0, stats.CacheHits);
    }

    [Fact]
    public async Task AcquireAsync_CacheHit_IncrementsCounter()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);
        await factory.AcquireAsync("test-user-1", "test-project-1");

        // Act
        await factory.AcquireAsync("test-user-1", "test-project-1");

        // Assert
        var stats = factory.GetStats();
        Assert.Equal(1, stats.CacheMisses);
        Assert.Equal(1, stats.CacheHits);
    }

    [Fact]
    public async Task MaxCacheSize_EvictsOldestIdleWorkspace()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions
        {
            MaxCachedWorkspaces = 2,
            IdleTimeoutMinutes = 0 // Immediate eviction when idle
        });
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Seed another project
        var project2 = new NovelProject
        {
            Id = "test-project-2",
            UserId = "test-user-1",
            Title = "Test Project 2",
            Genre = "SciFi",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.NovelProjects.Add(project2);

        var project3 = new NovelProject
        {
            Id = "test-project-3",
            UserId = "test-user-1",
            Title = "Test Project 3",
            Genre = "Mystery",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.NovelProjects.Add(project3);
        _dbContext.SaveChanges();

        // Act
        var entry1 = await factory.AcquireAsync("test-user-1", "test-project-1");
        factory.Release("test-user-1", "test-project-1");
        await Task.Delay(100); // Ensure time difference

        var entry2 = await factory.AcquireAsync("test-user-1", "test-project-2");
        factory.Release("test-user-1", "test-project-2");
        await Task.Delay(100);

        // This should evict project-1 since it's the oldest idle
        var entry3 = await factory.AcquireAsync("test-user-1", "test-project-3");

        // Assert
        var stats = factory.GetStats();
        Assert.Equal(2, stats.TotalWorkspaces); // Only 2 in cache
        Assert.True(stats.TotalEvictions >= 1); // At least one eviction
    }

    [Fact]
    public async Task ActiveReferences_PreventEviction()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions
        {
            MaxCachedWorkspaces = 1,
            IdleTimeoutMinutes = 0
        });
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Seed another project
        var project2 = new NovelProject
        {
            Id = "test-project-2",
            UserId = "test-user-1",
            Title = "Test Project 2",
            Genre = "SciFi",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.NovelProjects.Add(project2);
        _dbContext.SaveChanges();

        // Act
        var entry1 = await factory.AcquireAsync("test-user-1", "test-project-1");
        // Don't release - keep active reference

        // Try to add second workspace (should fail to evict first due to active reference)
        var entry2 = await factory.AcquireAsync("test-user-1", "test-project-2");

        // Assert
        var stats = factory.GetStats();
        Assert.Equal(2, stats.TotalWorkspaces); // Both remain because entry1 has active reference
        Assert.Equal(2, stats.ActiveReferences);
    }

    [Fact]
    public async Task ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions
        {
            MaxCachedWorkspaces = 50
        });
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Act - 100 concurrent acquire operations
        var tasks = new Task<WorkspaceEntry>[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = factory.AcquireAsync("test-user-1", "test-project-1");
        }

        var entries = await Task.WhenAll(tasks);

        // Assert
        // All should be the same instance
        Assert.All(entries, e => Assert.Same(entries[0], e));
        Assert.Equal(100, entries[0].ActiveReferences);

        var stats = factory.GetStats();
        Assert.Equal(1, stats.TotalWorkspaces);
        Assert.Equal(100, stats.ActiveReferences);
    }

    [Fact]
    public async Task ConcurrentReleases_ThreadSafe()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Acquire 100 times
        for (int i = 0; i < 100; i++)
        {
            await factory.AcquireAsync("test-user-1", "test-project-1");
        }

        // Act - 100 concurrent releases
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = Task.Run(() => factory.Release("test-user-1", "test-project-1"));
        }

        await Task.WhenAll(tasks);

        // Assert
        var stats = factory.GetStats();
        Assert.Equal(0, stats.ActiveReferences);
        Assert.Equal(1, stats.IdleWorkspaces);
    }

    [Fact]
    public async Task MultipleUsers_IsolatedWorkspaces()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Seed second user and project
        var user2 = new User
        {
            Id = "test-user-2",
            Username = "testuser2",
            Email = "test2@example.com",
            PasswordHash = "hash",
            Role = "author",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user2);

        var project2 = new NovelProject
        {
            Id = "test-project-2",
            UserId = "test-user-2",
            Title = "Test Project 2",
            Genre = "Fantasy",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.NovelProjects.Add(project2);
        _dbContext.SaveChanges();

        // Act
        var entry1 = await factory.AcquireAsync("test-user-1", "test-project-1");
        var entry2 = await factory.AcquireAsync("test-user-2", "test-project-2");

        // Assert
        Assert.NotSame(entry1, entry2);
        Assert.Equal("test-user-1", entry1.UserId);
        Assert.Equal("test-user-2", entry2.UserId);

        var stats = factory.GetStats();
        Assert.Equal(2, stats.TotalWorkspaces);
    }

    [Fact]
    public async Task AcquireAsync_InvalidProject_ThrowsException()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await factory.AcquireAsync("test-user-1", "non-existent-project"));
    }

    [Fact]
    public async Task AcquireAsync_WrongUser_ThrowsException()
    {
        // Arrange
        var options = Options.Create(new WorkspaceFactoryOptions());
        using var factory = new WorkspaceFactory(options, _serviceProvider);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await factory.AcquireAsync("wrong-user", "test-project-1"));
    }
}

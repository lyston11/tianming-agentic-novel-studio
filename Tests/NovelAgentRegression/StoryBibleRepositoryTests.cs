using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Repositories;
using TM.Tests.NovelAgentRegression.Helpers;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

/// <summary>
/// Integration tests for StoryBibleRepository using in-memory SQLite database.
/// Tests all CRUD operations and user isolation guarantees.
/// </summary>
public class StoryBibleRepositoryTests : IDisposable
{
    private readonly TestDbContext _testDb;
    private readonly NovelAgentDbContext _dbContext;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<StoryBibleRepository>> _loggerMock;
    private readonly StoryBibleRepository _repository;

    private const string User1Id = "user-1";
    private const string User2Id = "user-2";
    private const string Project1Id = "project-1";
    private const string Project2Id = "project-2";

    public StoryBibleRepositoryTests()
    {
        _testDb = TestDbContextFactory.CreateDisposableContext();
        _dbContext = _testDb.Context;

        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<StoryBibleRepository>>();

        _repository = new StoryBibleRepository(
            _dbContext,
            _currentUserServiceMock.Object,
            _loggerMock.Object);

        SeedTestData();
    }

    private void SeedTestData()
    {
        // Create users
        var user1 = new User
        {
            Id = User1Id,
            Username = "user1",
            Email = "user1@test.com",
            PasswordHash = "hash1",
            Role = "author",
            CreatedAt = DateTime.UtcNow
        };

        var user2 = new User
        {
            Id = User2Id,
            Username = "user2",
            Email = "user2@test.com",
            PasswordHash = "hash2",
            Role = "author",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.AddRange(user1, user2);

        // Create projects
        var project1 = new NovelProject
        {
            Id = Project1Id,
            UserId = User1Id,
            Title = "User 1 Project",
            Genre = "Fantasy",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var project2 = new NovelProject
        {
            Id = Project2Id,
            UserId = User2Id,
            Title = "User 2 Project",
            Genre = "SciFi",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.NovelProjects.AddRange(project1, project2);
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _testDb?.Dispose();
    }

    [Fact]
    public async Task LoadStoryBibleAsync_EmptyProject_ReturnsEmptyStoryBible()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);

        // Act
        var storyBible = await _repository.LoadStoryBibleAsync(Project1Id);

        // Assert
        Assert.NotNull(storyBible);
        Assert.Null(storyBible.Constitution);
        Assert.Empty(storyBible.VolumeArcs);
        Assert.Empty(storyBible.Characters);
        Assert.Empty(storyBible.ForeshadowEntries);
        Assert.Empty(storyBible.WorldSettings);
        Assert.Empty(storyBible.AgentRuns);
    }

    [Fact]
    public async Task LoadStoryBibleAsync_NonExistentProject_ThrowsKeyNotFoundException()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _repository.LoadStoryBibleAsync("non-existent"));
    }

    [Fact]
    public async Task LoadStoryBibleAsync_WrongUser_ThrowsKeyNotFoundException()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);

        // Act & Assert - User 1 trying to access User 2's project
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _repository.LoadStoryBibleAsync(Project2Id));
    }

    [Fact]
    public async Task SaveConstitutionAsync_NewConstitution_CreatesSuccessfully()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var constitution = new StoryConstitution
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            Genre = "Fantasy",
            CoreHook = "Test Hook",
            TargetAudience = "Test Audience"
        };

        // Act
        await _repository.SaveConstitutionAsync(constitution);

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        Assert.NotNull(loaded.Constitution);
        Assert.Equal("Test Hook", loaded.Constitution.CoreHook);
        Assert.Equal("Fantasy", loaded.Constitution.Genre);
    }

    [Fact]
    public async Task SaveConstitutionAsync_UpdateExisting_UpdatesSuccessfully()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var constitution = new StoryConstitution
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            Genre = "Fantasy",
            CoreHook = "Original Hook"
        };
        await _repository.SaveConstitutionAsync(constitution);

        // Act - Update
        constitution.CoreHook = "Updated Hook";
        await _repository.SaveConstitutionAsync(constitution);

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        Assert.NotNull(loaded.Constitution);
        Assert.Equal("Updated Hook", loaded.Constitution.CoreHook);
    }

    [Fact]
    public async Task SaveVolumeArcAsync_NewVolumeArc_CreatesSuccessfully()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var volumeArc = new VolumeArc
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            VolumeNumber = 1,
            VolumeTitle = "Volume 1",
            VolumeTheme = "First volume",
            Status = "planning"
        };

        // Act
        await _repository.SaveVolumeArcAsync(volumeArc);

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        Assert.Single(loaded.VolumeArcs);
        Assert.Equal("Volume 1", loaded.VolumeArcs[0].VolumeTitle);
    }

    [Fact]
    public async Task SaveVolumeArcAsync_MultipleVolumes_OrderedByVolumeNumber()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);

        // Act - Add volumes out of order
        await _repository.SaveVolumeArcAsync(new VolumeArc
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            VolumeNumber = 3,
            VolumeTitle = "Volume 3"
        });

        await _repository.SaveVolumeArcAsync(new VolumeArc
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            VolumeNumber = 1,
            VolumeTitle = "Volume 1"
        });

        await _repository.SaveVolumeArcAsync(new VolumeArc
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            VolumeNumber = 2,
            VolumeTitle = "Volume 2"
        });

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        Assert.Equal(3, loaded.VolumeArcs.Count);
        Assert.Equal("Volume 1", loaded.VolumeArcs[0].VolumeTitle);
        Assert.Equal("Volume 2", loaded.VolumeArcs[1].VolumeTitle);
        Assert.Equal("Volume 3", loaded.VolumeArcs[2].VolumeTitle);
    }

    [Fact]
    public async Task SaveCharacterAsync_NewCharacter_CreatesSuccessfully()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var character = new Character
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            Name = "Test Character",
            Role = "protagonist",
            Personality = "Test description",
            Status = "active"
        };

        // Act
        await _repository.SaveCharacterAsync(character);

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        Assert.Single(loaded.Characters);
        Assert.Equal("Test Character", loaded.Characters[0].Name);
    }

    [Fact]
    public async Task SaveCharacterAsync_UpdateCharacter_UpdatesSuccessfully()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var characterId = Guid.NewGuid().ToString();
        var character = new Character
        {
            Id = characterId,
            ProjectId = Project1Id,
            UserId = User1Id,
            Name = "Test Character",
            Status = "active"
        };
        await _repository.SaveCharacterAsync(character);

        // Act - Update
        character.Status = "inactive";
        await _repository.SaveCharacterAsync(character);

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        Assert.Single(loaded.Characters);
        Assert.Equal("inactive", loaded.Characters[0].Status);
    }

    [Fact]
    public async Task PlantForeshadowAsync_NewForeshadow_CreatesSuccessfully()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var foreshadow = new ForeshadowEntry
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            Title = "Test Foreshadow",
            PlantedInChapter = "Chapter 1",
            PlantedContext = "Some context",
            Content = "Expected resolution in Chapter 10",
            Category = "plot"
        };

        // Act
        await _repository.PlantForeshadowAsync(foreshadow);

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        Assert.Single(loaded.ForeshadowEntries);
        Assert.Equal("Test Foreshadow", loaded.ForeshadowEntries[0].Title);
        Assert.Equal("planted", loaded.ForeshadowEntries[0].Status);
        Assert.NotNull(loaded.ForeshadowEntries[0].PlantedAt);
    }

    [Fact]
    public async Task ResolveForeshadowAsync_ExistingForeshadow_ResolvesSuccessfully()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var foreshadowId = Guid.NewGuid().ToString();
        var foreshadow = new ForeshadowEntry
        {
            Id = foreshadowId,
            ProjectId = Project1Id,
            UserId = User1Id,
            Title = "Test Foreshadow",
            PlantedInChapter = "Chapter 1"
        };
        await _repository.PlantForeshadowAsync(foreshadow);

        // Act
        await _repository.ResolveForeshadowAsync(foreshadowId, "Chapter 10", "Resolution context");

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        var resolved = loaded.ForeshadowEntries[0];
        Assert.Equal("resolved", resolved.Status);
        Assert.Equal("Chapter 10", resolved.ResolvedInChapter);
        Assert.Equal("Resolution context", resolved.ResolvedContext);
        Assert.NotNull(resolved.ResolvedAt);
    }

    [Fact]
    public async Task ResolveForeshadowAsync_NonExistent_ThrowsKeyNotFoundException()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _repository.ResolveForeshadowAsync("non-existent", "Chapter 10", null));
    }

    [Fact]
    public async Task SaveWorldSettingAsync_NewSetting_CreatesSuccessfully()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var worldSetting = new WorldSettingEntry
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            Category = "magic_system",
            Title = "Elemental Magic",
            Content = "Fire, water, earth, air",
            SubCategory = "global"
        };

        // Act
        await _repository.SaveWorldSettingAsync(worldSetting);

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        Assert.Single(loaded.WorldSettings);
        Assert.Equal("Elemental Magic", loaded.WorldSettings[0].Title);
        Assert.Equal(1, loaded.WorldSettings[0].Version);
    }

    [Fact]
    public async Task SaveWorldSettingAsync_UpdateSetting_IncrementsVersion()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var settingId = Guid.NewGuid().ToString();
        var worldSetting = new WorldSettingEntry
        {
            Id = settingId,
            ProjectId = Project1Id,
            UserId = User1Id,
            Title = "Test Setting",
            Content = "Original content"
        };
        await _repository.SaveWorldSettingAsync(worldSetting);

        // Act - Update
        worldSetting.Content = "Updated content";
        await _repository.SaveWorldSettingAsync(worldSetting);

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        var updated = loaded.WorldSettings[0];
        Assert.Equal("Updated content", updated.Content);
        Assert.Equal(2, updated.Version);
        Assert.Equal("Original content", updated.PreviousVersion);
    }

    [Fact]
    public async Task SaveAgentRunAsync_NewRun_CreatesSuccessfully()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var agentRun = new AgentRun
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            RunType = "chapter_generation",
            Status = "completed",
            InputParams = "{\"prompt\":\"Write chapter 1\"}",
            OutputData = "{\"content\":\"Chapter 1 content\"}",
            StartedAt = DateTime.UtcNow
        };

        // Act
        await _repository.SaveAgentRunAsync(agentRun);

        // Assert
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);
        Assert.Single(loaded.AgentRuns);
        Assert.Equal("chapter_generation", loaded.AgentRuns[0].RunType);
    }

    [Fact]
    public async Task UserIsolation_User1CannotAccessUser2Data()
    {
        // Arrange - User 2 creates data
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User2Id);
        var constitution = new StoryConstitution
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project2Id,
            UserId = User2Id,
            Genre = "Fantasy",
            CoreHook = "User 2 Hook"
        };
        await _repository.SaveConstitutionAsync(constitution);

        // Act - User 1 tries to access User 2's project
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _repository.LoadStoryBibleAsync(Project2Id));
    }

    [Fact]
    public async Task UserIsolation_MultipleUsers_SeparateData()
    {
        // Arrange & Act
        // User 1 creates data
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        await _repository.SaveConstitutionAsync(new StoryConstitution
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            Genre = "Fantasy",
            CoreHook = "User 1 Hook"
        });

        // User 2 creates data
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User2Id);
        await _repository.SaveConstitutionAsync(new StoryConstitution
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project2Id,
            UserId = User2Id,
            Genre = "SciFi",
            CoreHook = "User 2 Hook"
        });

        // Assert - Each user sees only their own data
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);
        var user1Bible = await _repository.LoadStoryBibleAsync(Project1Id);
        Assert.Equal("User 1 Hook", user1Bible.Constitution!.CoreHook);

        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User2Id);
        var user2Bible = await _repository.LoadStoryBibleAsync(Project2Id);
        Assert.Equal("User 2 Hook", user2Bible.Constitution!.CoreHook);
    }

    [Fact]
    public async Task LoadStoryBibleAsync_CompleteStoryBible_LoadsAllEntities()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.GetUserId()).Returns(User1Id);

        // Create complete story bible
        await _repository.SaveConstitutionAsync(new StoryConstitution
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            Genre = "Fantasy",
            CoreHook = "Test Hook"
        });

        await _repository.SaveVolumeArcAsync(new VolumeArc
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            VolumeNumber = 1,
            VolumeTitle = "Volume 1"
        });

        await _repository.SaveCharacterAsync(new Character
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            Name = "Hero"
        });

        await _repository.PlantForeshadowAsync(new ForeshadowEntry
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            Title = "Foreshadow 1",
            PlantedInChapter = "Chapter 1"
        });

        await _repository.SaveWorldSettingAsync(new WorldSettingEntry
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            Title = "Magic System"
        });

        await _repository.SaveAgentRunAsync(new AgentRun
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = Project1Id,
            UserId = User1Id,
            RunType = "chapter_generation",
            Status = "completed",
            StartedAt = DateTime.UtcNow
        });

        // Act
        var loaded = await _repository.LoadStoryBibleAsync(Project1Id);

        // Assert
        Assert.NotNull(loaded.Constitution);
        Assert.Single(loaded.VolumeArcs);
        Assert.Single(loaded.Characters);
        Assert.Single(loaded.ForeshadowEntries);
        Assert.Single(loaded.WorldSettings);
        Assert.Single(loaded.AgentRuns);
    }
}

using Microsoft.EntityFrameworkCore;
using TM.Tests.NovelAgentRegression.Helpers;
using TM.Web.NovelAgentWeb.Data.Entities;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Data;

/// <summary>
/// Unit tests for NovelProject entity CRUD operations and cascade delete behavior.
/// Tests verify that deleting a project cascades to all child entities.
/// </summary>
public class ProjectRepositoryTests
{
    [Fact]
    public async Task Test_CreateProject_Success()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "projectowner",
            Email = "owner@example.com",
            PasswordHash = "hash",
            Role = "User"
        };

        var project = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Title = "Test Novel",
            Genre = "Fantasy",
            SubGenre = "Epic Fantasy",
            CoreHook = "A hero's journey",
            Status = "draft",
            WordCount = 0,
            StorageProjectName = "test-novel-001"
        };

        // Act
        context.Users.Add(user);
        context.NovelProjects.Add(project);
        await context.SaveChangesAsync();

        // Assert
        var savedProject = await context.NovelProjects.FindAsync(project.Id);
        Assert.NotNull(savedProject);
        Assert.Equal("Test Novel", savedProject.Title);
        Assert.Equal("Fantasy", savedProject.Genre);
        Assert.Equal(user.Id, savedProject.UserId);
    }

    [Fact]
    public async Task Test_DeleteProject_CascadesToChapters()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user",
            Email = "user@example.com",
            PasswordHash = "hash",
            Role = "User"
        };

        var project = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Title = "Novel with Chapters",
            StorageProjectName = "novel-chapters"
        };

        var chapters = new[]
        {
            new Chapter { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Title = "Chapter 1", ChapterNumber = 1, ContentPath = "/ch1.txt" },
            new Chapter { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Title = "Chapter 2", ChapterNumber = 2, ContentPath = "/ch2.txt" },
            new Chapter { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Title = "Chapter 3", ChapterNumber = 3, ContentPath = "/ch3.txt" }
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Chapters.AddRange(chapters);
        await context.SaveChangesAsync();

        // Verify chapters exist
        var chapterCount = await context.Chapters.CountAsync(c => c.ProjectId == project.Id);
        Assert.Equal(3, chapterCount);

        // Act - Delete project
        context.NovelProjects.Remove(project);
        await context.SaveChangesAsync();

        // Assert - All chapters should be cascade deleted
        var remainingChapters = await context.Chapters.Where(c => c.ProjectId == project.Id).ToListAsync();
        Assert.Empty(remainingChapters);
    }

    [Fact]
    public async Task Test_DeleteProject_CascadesToForeshadows()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user",
            Email = "user@example.com",
            PasswordHash = "hash",
            Role = "User"
        };

        var project = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Title = "Novel with Foreshadows",
            StorageProjectName = "novel-foreshadows"
        };

        var foreshadows = new[]
        {
            new Foreshadow { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Name = "Prophecy", Type = "plot", Status = "planned" },
            new Foreshadow { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Name = "Mystery Box", Type = "mystery", Status = "setup" }
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Foreshadows.AddRange(foreshadows);
        await context.SaveChangesAsync();

        // Verify foreshadows exist
        var foreshadowCount = await context.Foreshadows.CountAsync(f => f.ProjectId == project.Id);
        Assert.Equal(2, foreshadowCount);

        // Act - Delete project
        context.NovelProjects.Remove(project);
        await context.SaveChangesAsync();

        // Assert - All foreshadows should be cascade deleted
        var remainingForeshadows = await context.Foreshadows.Where(f => f.ProjectId == project.Id).ToListAsync();
        Assert.Empty(remainingForeshadows);
    }

    [Fact]
    public async Task Test_DeleteProject_CascadesToCharacters()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user",
            Email = "user@example.com",
            PasswordHash = "hash",
            Role = "User"
        };

        var project = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Title = "Novel with Characters",
            StorageProjectName = "novel-characters"
        };

        var characters = new[]
        {
            new Character { Id = Guid.NewGuid().ToString(), UserId = user.Id, ProjectId = project.Id, Name = "Protagonist", Role = "hero" },
            new Character { Id = Guid.NewGuid().ToString(), UserId = user.Id, ProjectId = project.Id, Name = "Antagonist", Role = "villain" },
            new Character { Id = Guid.NewGuid().ToString(), UserId = user.Id, ProjectId = project.Id, Name = "Mentor", Role = "supporting" }
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Characters.AddRange(characters);
        await context.SaveChangesAsync();

        // Verify characters exist
        var characterCount = await context.Characters.CountAsync(c => c.ProjectId == project.Id);
        Assert.Equal(3, characterCount);

        // Act - Delete project
        context.NovelProjects.Remove(project);
        await context.SaveChangesAsync();

        // Assert - All characters should be cascade deleted
        var remainingCharacters = await context.Characters.Where(c => c.ProjectId == project.Id).ToListAsync();
        Assert.Empty(remainingCharacters);
    }

    [Fact]
    public async Task Test_DeleteProject_CascadesToAllChildEntities()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user",
            Email = "user@example.com",
            PasswordHash = "hash",
            Role = "User"
        };

        var project = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Title = "Complete Novel",
            StorageProjectName = "complete-novel"
        };

        var chapter = new Chapter
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Title = "Chapter 1",
            ChapterNumber = 1,
            ContentPath = "/ch1.txt"
        };

        var foreshadow = new Foreshadow
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Name = "Plot Twist",
            Type = "plot"
        };

        var character = new Character
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            ProjectId = project.Id,
            Name = "Hero",
            Role = "protagonist"
        };

        var worldSetting = new WorldSettingEntry
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            ProjectId = project.Id,
            Category = "magic",
            Title = "Magic System",
            Content = "Magic system details"
        };

        var knowledgeBase = new KnowledgeBase
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            EntryType = "lore",
            Title = "Ancient History",
            Content = "Long ago..."
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Chapters.Add(chapter);
        context.Foreshadows.Add(foreshadow);
        context.Characters.Add(character);
        context.WorldSettingEntries.Add(worldSetting);
        context.KnowledgeBases.Add(knowledgeBase);
        await context.SaveChangesAsync();

        // Verify all entities exist
        Assert.NotNull(await context.Chapters.FindAsync(chapter.Id));
        Assert.NotNull(await context.Foreshadows.FindAsync(foreshadow.Id));
        Assert.NotNull(await context.Characters.FindAsync(character.Id));
        Assert.NotNull(await context.WorldSettingEntries.FindAsync(worldSetting.Id));
        Assert.NotNull(await context.KnowledgeBases.FindAsync(knowledgeBase.Id));

        // Act - Delete project
        context.NovelProjects.Remove(project);
        await context.SaveChangesAsync();

        // Assert - All child entities should be cascade deleted
        Assert.Null(await context.Chapters.FindAsync(chapter.Id));
        Assert.Null(await context.Foreshadows.FindAsync(foreshadow.Id));
        Assert.Null(await context.Characters.FindAsync(character.Id));
        Assert.Null(await context.WorldSettingEntries.FindAsync(worldSetting.Id));
        Assert.Null(await context.KnowledgeBases.FindAsync(knowledgeBase.Id));
    }

    [Fact]
    public async Task Test_UpdateProject_Success()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user",
            Email = "user@example.com",
            PasswordHash = "hash",
            Role = "User"
        };

        var project = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Title = "Original Title",
            Status = "draft",
            WordCount = 1000,
            StorageProjectName = "original"
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        await context.SaveChangesAsync();

        // Act
        project.Title = "Updated Title";
        project.Status = "published";
        project.WordCount = 50000;
        project.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        // Assert
        var updatedProject = await context.NovelProjects.FindAsync(project.Id);
        Assert.NotNull(updatedProject);
        Assert.Equal("Updated Title", updatedProject.Title);
        Assert.Equal("published", updatedProject.Status);
        Assert.Equal(50000, updatedProject.WordCount);
    }

    [Fact]
    public async Task Test_QueryProjects_ByUserId()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user1 = new User { Id = Guid.NewGuid().ToString(), Username = "user1", Email = "user1@example.com", PasswordHash = "hash", Role = "User" };
        var user2 = new User { Id = Guid.NewGuid().ToString(), Username = "user2", Email = "user2@example.com", PasswordHash = "hash", Role = "User" };

        var projects = new[]
        {
            new NovelProject { Id = Guid.NewGuid().ToString(), UserId = user1.Id, Title = "User1 Project1", StorageProjectName = "u1p1" },
            new NovelProject { Id = Guid.NewGuid().ToString(), UserId = user1.Id, Title = "User1 Project2", StorageProjectName = "u1p2" },
            new NovelProject { Id = Guid.NewGuid().ToString(), UserId = user2.Id, Title = "User2 Project1", StorageProjectName = "u2p1" }
        };

        context.Users.AddRange(user1, user2);
        context.NovelProjects.AddRange(projects);
        await context.SaveChangesAsync();

        // Act
        var user1Projects = await context.NovelProjects.Where(p => p.UserId == user1.Id).ToListAsync();

        // Assert
        Assert.Equal(2, user1Projects.Count);
        Assert.All(user1Projects, p => Assert.Equal(user1.Id, p.UserId));
    }

    [Fact]
    public async Task Test_UniqueStorageProjectName_Constraint()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user",
            Email = "user@example.com",
            PasswordHash = "hash",
            Role = "User"
        };

        var project1 = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Title = "Project 1",
            StorageProjectName = "duplicate-storage-name"
        };

        var project2 = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Title = "Project 2",
            StorageProjectName = "duplicate-storage-name" // Same storage name
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project1);
        await context.SaveChangesAsync();

        // Act & Assert
        context.NovelProjects.Add(project2);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }
}

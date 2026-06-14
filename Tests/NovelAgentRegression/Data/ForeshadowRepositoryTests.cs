using Microsoft.EntityFrameworkCore;
using TM.Tests.NovelAgentRegression.Helpers;
using TM.Web.NovelAgentWeb.Data.Entities;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Data;

/// <summary>
/// Unit tests for Foreshadow entity and foreign key constraint behavior.
/// Tests verify that deleting chapters sets FK to NULL (not cascade delete).
/// </summary>
public class ForeshadowRepositoryTests
{
    [Fact]
    public async Task Test_CreateForeshadow_Success()
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
            Title = "Novel"
        };

        var chapter = new Chapter
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Title = "Chapter 1",
            ChapterNumber = 1
        };

        var foreshadow = new Foreshadow
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Name = "The Prophecy",
            Type = "plot",
            Status = "setup",
            SetupChapterId = chapter.Id,
            Importance = 5,
            Description = "A mysterious prophecy is revealed"
        };

        // Act
        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Chapters.Add(chapter);
        context.Foreshadows.Add(foreshadow);
        await context.SaveChangesAsync();

        // Assert
        var savedForeshadow = await context.Foreshadows.FindAsync(foreshadow.Id);
        Assert.NotNull(savedForeshadow);
        Assert.Equal("The Prophecy", savedForeshadow.Name);
        Assert.Equal(chapter.Id, savedForeshadow.SetupChapterId);
        Assert.Equal(5, savedForeshadow.Importance);
    }

    [Fact]
    public async Task Test_DeleteSetupChapter_SetsFKToNull_ForeshadowPreserved()
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
            Title = "Novel"
        };

        var setupChapter = new Chapter
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Title = "Setup Chapter",
            ChapterNumber = 1
        };

        var foreshadow = new Foreshadow
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Name = "Mystery",
            Type = "plot",
            SetupChapterId = setupChapter.Id
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Chapters.Add(setupChapter);
        context.Foreshadows.Add(foreshadow);
        await context.SaveChangesAsync();

        // Verify initial state
        var initialForeshadow = await context.Foreshadows.FindAsync(foreshadow.Id);
        Assert.NotNull(initialForeshadow);
        Assert.Equal(setupChapter.Id, initialForeshadow.SetupChapterId);

        // Act - Delete setup chapter
        context.Chapters.Remove(setupChapter);
        await context.SaveChangesAsync();

        // Assert - Foreshadow should still exist with SetupChapterId set to NULL
        var preservedForeshadow = await context.Foreshadows.FindAsync(foreshadow.Id);
        Assert.NotNull(preservedForeshadow);
        Assert.Null(preservedForeshadow.SetupChapterId);
        Assert.Equal("Mystery", preservedForeshadow.Name);
    }

    [Fact]
    public async Task Test_DeletePayoffChapter_SetsFKToNull_ForeshadowPreserved()
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
            Title = "Novel"
        };

        var payoffChapter = new Chapter
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Title = "Payoff Chapter",
            ChapterNumber = 10
        };

        var foreshadow = new Foreshadow
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Name = "Revelation",
            Type = "plot",
            PayoffChapterId = payoffChapter.Id
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Chapters.Add(payoffChapter);
        context.Foreshadows.Add(foreshadow);
        await context.SaveChangesAsync();

        // Verify initial state
        var initialForeshadow = await context.Foreshadows.FindAsync(foreshadow.Id);
        Assert.NotNull(initialForeshadow);
        Assert.Equal(payoffChapter.Id, initialForeshadow.PayoffChapterId);

        // Act - Delete payoff chapter
        context.Chapters.Remove(payoffChapter);
        await context.SaveChangesAsync();

        // Assert - Foreshadow should still exist with PayoffChapterId set to NULL
        var preservedForeshadow = await context.Foreshadows.FindAsync(foreshadow.Id);
        Assert.NotNull(preservedForeshadow);
        Assert.Null(preservedForeshadow.PayoffChapterId);
        Assert.Equal("Revelation", preservedForeshadow.Name);
    }

    [Fact]
    public async Task Test_DeleteBothChapters_SetsBothFKsToNull()
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
            Title = "Novel"
        };

        var setupChapter = new Chapter
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Title = "Setup",
            ChapterNumber = 1
        };

        var payoffChapter = new Chapter
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Title = "Payoff",
            ChapterNumber = 10
        };

        var foreshadow = new Foreshadow
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Name = "Complete Arc",
            Type = "plot",
            SetupChapterId = setupChapter.Id,
            PayoffChapterId = payoffChapter.Id
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Chapters.AddRange(setupChapter, payoffChapter);
        context.Foreshadows.Add(foreshadow);
        await context.SaveChangesAsync();

        // Act - Delete both chapters
        context.Chapters.RemoveRange(setupChapter, payoffChapter);
        await context.SaveChangesAsync();

        // Assert - Foreshadow should still exist with both FKs set to NULL
        var preservedForeshadow = await context.Foreshadows.FindAsync(foreshadow.Id);
        Assert.NotNull(preservedForeshadow);
        Assert.Null(preservedForeshadow.SetupChapterId);
        Assert.Null(preservedForeshadow.PayoffChapterId);
    }

    [Fact]
    public async Task Test_DeleteProject_CascadeDeletesForeshadowsEvenWithChapterReferences()
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
            Title = "Novel"
        };

        var chapter = new Chapter
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Title = "Chapter",
            ChapterNumber = 1
        };

        var foreshadow = new Foreshadow
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Name = "Foreshadow",
            SetupChapterId = chapter.Id
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Chapters.Add(chapter);
        context.Foreshadows.Add(foreshadow);
        await context.SaveChangesAsync();

        // Act - Delete project (should cascade to both chapter and foreshadow)
        context.NovelProjects.Remove(project);
        await context.SaveChangesAsync();

        // Assert - Both chapter and foreshadow should be deleted
        Assert.Null(await context.Chapters.FindAsync(chapter.Id));
        Assert.Null(await context.Foreshadows.FindAsync(foreshadow.Id));
    }

    [Fact]
    public async Task Test_UpdateForeshadow_ChangeChapterReferences()
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
            Title = "Novel"
        };

        var chapter1 = new Chapter { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Title = "Ch1", ChapterNumber = 1 };
        var chapter2 = new Chapter { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Title = "Ch2", ChapterNumber = 2 };

        var foreshadow = new Foreshadow
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Name = "Foreshadow",
            SetupChapterId = chapter1.Id,
            Status = "planned"
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Chapters.AddRange(chapter1, chapter2);
        context.Foreshadows.Add(foreshadow);
        await context.SaveChangesAsync();

        // Act - Update foreshadow to reference different chapter and change status
        foreshadow.SetupChapterId = chapter2.Id;
        foreshadow.PayoffChapterId = chapter2.Id;
        foreshadow.Status = "setup";
        await context.SaveChangesAsync();

        // Assert
        var updatedForeshadow = await context.Foreshadows.FindAsync(foreshadow.Id);
        Assert.NotNull(updatedForeshadow);
        Assert.Equal(chapter2.Id, updatedForeshadow.SetupChapterId);
        Assert.Equal(chapter2.Id, updatedForeshadow.PayoffChapterId);
        Assert.Equal("setup", updatedForeshadow.Status);
    }

    [Fact]
    public async Task Test_QueryForeshadows_ByStatus()
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
            Title = "Novel"
        };

        var foreshadows = new[]
        {
            new Foreshadow { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Name = "F1", Status = "planned" },
            new Foreshadow { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Name = "F2", Status = "setup" },
            new Foreshadow { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Name = "F3", Status = "resolved" },
            new Foreshadow { Id = Guid.NewGuid().ToString(), ProjectId = project.Id, Name = "F4", Status = "setup" }
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Foreshadows.AddRange(foreshadows);
        await context.SaveChangesAsync();

        // Act
        var setupForeshadows = await context.Foreshadows.Where(f => f.Status == "setup").ToListAsync();

        // Assert
        Assert.Equal(2, setupForeshadows.Count);
        Assert.All(setupForeshadows, f => Assert.Equal("setup", f.Status));
    }

    [Fact]
    public async Task Test_Character_DeleteChapter_SetsFKToNull()
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
            Title = "Novel"
        };

        var chapter = new Chapter
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Title = "First Appearance",
            ChapterNumber = 1
        };

        var character = new Character
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            ProjectId = project.Id,
            Name = "Hero",
            Role = "protagonist",
            FirstAppearChapter = chapter.Id
        };

        context.Users.Add(user);
        context.NovelProjects.Add(project);
        context.Chapters.Add(chapter);
        context.Characters.Add(character);
        await context.SaveChangesAsync();

        // Verify initial state
        var initialCharacter = await context.Characters.FindAsync(character.Id);
        Assert.Equal(chapter.Id, initialCharacter!.FirstAppearChapter);

        // Act - Delete chapter
        context.Chapters.Remove(chapter);
        await context.SaveChangesAsync();

        // Assert - Character should still exist with FirstAppearChapter set to NULL
        var preservedCharacter = await context.Characters.FindAsync(character.Id);
        Assert.NotNull(preservedCharacter);
        Assert.Null(preservedCharacter.FirstAppearChapter);
        Assert.Equal("Hero", preservedCharacter.Name);
    }
}

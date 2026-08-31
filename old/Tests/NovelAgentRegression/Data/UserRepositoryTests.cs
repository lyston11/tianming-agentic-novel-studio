using Microsoft.EntityFrameworkCore;
using TM.Tests.NovelAgentRegression.Helpers;
using TM.Web.NovelAgentWeb.Data.Entities;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Data;

/// <summary>
/// Unit tests for User entity CRUD operations, unique constraints, and data validation.
/// </summary>
public class UserRepositoryTests
{
    [Fact]
    public async Task Test_CreateUser_Success()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hashed_password_123",
            Role = "User",
            StorageQuotaMb = 5120,
            ApiCallQuota = 10000,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Assert
        var savedUser = await context.Users.FindAsync(user.Id);
        Assert.NotNull(savedUser);
        Assert.Equal("testuser", savedUser.Username);
        Assert.Equal("test@example.com", savedUser.Email);
        Assert.Equal("hashed_password_123", savedUser.PasswordHash);
        Assert.Equal("User", savedUser.Role);
        Assert.Equal(5120, savedUser.StorageQuotaMb);
        Assert.Equal(10000, savedUser.ApiCallQuota);
        Assert.True(savedUser.IsActive);
    }

    [Fact]
    public async Task Test_CreateUser_DuplicateUsername_ThrowsException()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user1 = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "duplicateuser",
            Email = "user1@example.com",
            PasswordHash = "hash1",
            Role = "User"
        };

        var user2 = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "duplicateuser", // Same username
            Email = "user2@example.com",
            PasswordHash = "hash2",
            Role = "User"
        };

        context.Users.Add(user1);
        await context.SaveChangesAsync();

        // Act & Assert
        context.Users.Add(user2);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Fact]
    public async Task Test_CreateUser_DuplicateEmail_ThrowsException()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user1 = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user1",
            Email = "duplicate@example.com",
            PasswordHash = "hash1",
            Role = "User"
        };

        var user2 = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user2",
            Email = "duplicate@example.com", // Same email
            PasswordHash = "hash2",
            Role = "User"
        };

        context.Users.Add(user1);
        await context.SaveChangesAsync();

        // Act & Assert
        context.Users.Add(user2);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Fact]
    public async Task Test_UpdateUser_Success()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "originaluser",
            Email = "original@example.com",
            PasswordHash = "hash",
            Role = "User",
            IsActive = true
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Act
        user.Email = "updated@example.com";
        user.IsActive = false;
        user.LastLoginAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        // Assert
        var updatedUser = await context.Users.FindAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.Equal("updated@example.com", updatedUser.Email);
        Assert.False(updatedUser.IsActive);
        Assert.NotNull(updatedUser.LastLoginAt);
    }

    [Fact]
    public async Task Test_DeleteUser_Success()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "deleteuser",
            Email = "delete@example.com",
            PasswordHash = "hash",
            Role = "User"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Act
        context.Users.Remove(user);
        await context.SaveChangesAsync();

        // Assert
        var deletedUser = await context.Users.FindAsync(user.Id);
        Assert.Null(deletedUser);
    }

    [Fact]
    public async Task Test_QueryUsers_ByUsername()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var users = new[]
        {
            new User { Id = Guid.NewGuid().ToString(), Username = "alice", Email = "alice@example.com", PasswordHash = "hash", Role = "User" },
            new User { Id = Guid.NewGuid().ToString(), Username = "bob", Email = "bob@example.com", PasswordHash = "hash", Role = "User" },
            new User { Id = Guid.NewGuid().ToString(), Username = "charlie", Email = "charlie@example.com", PasswordHash = "hash", Role = "Admin" }
        };

        context.Users.AddRange(users);
        await context.SaveChangesAsync();

        // Act
        var foundUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "bob");

        // Assert
        Assert.NotNull(foundUser);
        Assert.Equal("bob", foundUser.Username);
        Assert.Equal("bob@example.com", foundUser.Email);
    }

    [Fact]
    public async Task Test_QueryUsers_ByRole()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var users = new[]
        {
            new User { Id = Guid.NewGuid().ToString(), Username = "user1", Email = "user1@example.com", PasswordHash = "hash", Role = "User" },
            new User { Id = Guid.NewGuid().ToString(), Username = "admin1", Email = "admin1@example.com", PasswordHash = "hash", Role = "Admin" },
            new User { Id = Guid.NewGuid().ToString(), Username = "user2", Email = "user2@example.com", PasswordHash = "hash", Role = "User" }
        };

        context.Users.AddRange(users);
        await context.SaveChangesAsync();

        // Act
        var adminUsers = await context.Users.Where(u => u.Role == "Admin").ToListAsync();

        // Assert
        Assert.Single(adminUsers);
        Assert.Equal("admin1", adminUsers[0].Username);
    }

    [Fact]
    public async Task Test_UserWithSettings_CascadeDelete()
    {
        // Arrange
        using var testDb = TestDbContextFactory.CreateDisposableContext();
        var context = testDb.Context;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "userWithSettings",
            Email = "user@example.com",
            PasswordHash = "hash",
            Role = "User"
        };

        var settings = new UserSettings
        {
            UserId = user.Id,
            LlmProvider = "OpenAI",
            LlmModel = "gpt-4",
            Theme = "dark",
            Language = "en-US"
        };

        context.Users.Add(user);
        context.UserSettings.Add(settings);
        await context.SaveChangesAsync();

        // Act - Delete user
        context.Users.Remove(user);
        await context.SaveChangesAsync();

        // Assert - Settings should be cascade deleted
        var deletedSettings = await context.UserSettings.FindAsync(user.Id);
        Assert.Null(deletedSettings);
    }
}

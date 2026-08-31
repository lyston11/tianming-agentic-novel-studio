using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.StoryBible;
using Xunit;

namespace Tests.Unit.Services.StoryBibleSpecs;

public sealed class StoryBibleServiceTests
{
    [Fact]
    public async Task CreateConstitutionAsync_WithSameIdempotencyKey_ReturnsExistingConstitution()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var service = CreateService(db);
        var request = new CreateStoryConstitutionRequest
        {
            ProjectId = "project-1",
            Genre = "末世",
            SubGenre = "废土升级",
            CoreHook = "旧邮路每开启一次都会暴露坐标。",
            ReaderPromise = "打怪升级和路线探索并行推进。",
            IdempotencyKey = "constitution-key-001"
        };

        var first = await service.CreateConstitutionAsync(request);
        var second = await service.CreateConstitutionAsync(request);

        Assert.Equal(first.Id, second.Id);
        var constitution = await db.StoryConstitutions.SingleAsync();
        Assert.Equal("constitution-key-001", constitution.IdempotencyKey);
    }

    [Fact]
    public async Task CreateCharacterAsync_WithSameIdempotencyKey_ReturnsExistingCharacter()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var service = CreateService(db);
        var request = new CreateCharacterRequest
        {
            ProjectId = "project-1",
            Name = "林昼",
            Role = "protagonist",
            CoreGoal = "重启旧邮路并找到失踪的妹妹。",
            Motivation = "每一次路线选择都要付出明确代价。",
            IdempotencyKey = "character-key-001"
        };

        var first = await service.CreateCharacterAsync(request);
        var second = await service.CreateCharacterAsync(request);

        Assert.Equal(first.Id, second.Id);
        var character = await db.Characters.SingleAsync();
        Assert.Equal("character-key-001", character.IdempotencyKey);
    }

    private static StoryBibleService CreateService(NovelAgentDbContext db)
    {
        return new StoryBibleService(
            db,
            new FixedCurrentUserService("user-1"),
            NullLogger<StoryBibleService>.Instance);
    }

    private static void SeedUserProject(NovelAgentDbContext db)
    {
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
            Title = "测试项目",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private sealed class FixedCurrentUserService : ICurrentUserService
    {
        private readonly string _userId;

        public FixedCurrentUserService(string userId)
        {
            _userId = userId;
        }

        public string GetUserId() => _userId;
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => _userId;
    }
}

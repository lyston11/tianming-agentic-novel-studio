using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionRelationStrengthSourceServiceTests
{
    [Fact]
    public void Constructor_FailsFastWhenProjectHasNoDatabaseScope()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProductionRelationStrengthSourceService((IServiceScopeFactory)null!, "user-1", "project-1"));

        Assert.Equal("scopeFactory", exception.ParamName);
    }

    [Fact]
    public async Task LoadRelationStrengthFactsAsync_ReadsCharacterRelationshipsFromCurrentProject()
    {
        await using var db = CreateDb();
        SeedCharacters(db);

        var source = new ProductionRelationStrengthSourceService(db, "user-1", "project-1");

        var facts = await source.LoadRelationStrengthFactsAsync();

        Assert.Contains(facts, fact =>
            fact.LeftId == "char-lin" &&
            fact.RightId == "char-mentor" &&
            fact.Strength == RelationStrength.Strong);
        Assert.Contains(facts, fact =>
            fact.LeftId == "char-lin" &&
            fact.RightId == "char-ally" &&
            fact.Strength == RelationStrength.Medium);
        Assert.DoesNotContain(facts, fact => fact.LeftId == "char-other" || fact.RightId == "char-other");
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedCharacters(NovelAgentDbContext db)
    {
        db.Users.AddRange(
            NewUser("user-1", "user1"),
            NewUser("user-2", "user2"));
        db.NovelProjects.AddRange(
            NewProject("project-1", "user-1", "旧邮路"),
            NewProject("project-2", "user-2", "别的书"));
        db.Characters.AddRange(
            NewCharacter("char-lin", "user-1", "project-1", "林澈", """
            [
              {"targetCharacterId":"char-mentor","relationshipType":"师徒"},
              {"targetCharacterId":"char-ally","relationshipType":"朋友"}
            ]
            """),
            NewCharacter("char-mentor", "user-1", "project-1", "周砚", null),
            NewCharacter("char-ally", "user-1", "project-1", "姜黎", null),
            NewCharacter("char-other", "user-2", "project-2", "沈烁", """
            [{"targetCharacterId":"char-lin","relationshipType":"宿敌"}]
            """));
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

    private static Character NewCharacter(
        string id,
        string userId,
        string projectId,
        string name,
        string? relationships) => new()
    {
        Id = id,
        UserId = userId,
        ProjectId = projectId,
        Name = name,
        Role = "supporting",
        Relationships = relationships,
        Status = "active",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

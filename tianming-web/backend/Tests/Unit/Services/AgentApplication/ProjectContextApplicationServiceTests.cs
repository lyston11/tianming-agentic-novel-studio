using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Tianming.NovelAgent.Application.Conversation;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentApplication;
using Xunit;

namespace Tests.Unit.Services.AgentApplication;

public sealed class ProjectContextApplicationServiceTests
{
    [Fact]
    public async Task ActivateAsync_RequiresAuditableUserConfirmationBeforeCallingStore()
    {
        var store = new Mock<IProjectContextStore>(MockBehavior.Strict);
        var service = new ProjectContextApplicationService(store.Object);

        var result = await service.ActivateAsync(new ActivateProjectContextCommand(
            "user-1",
            "session-1",
            "project-1",
            "activate-1",
            0));

        Assert.False(result.Succeeded);
        Assert.True(result.Recoverable);
        Assert.Equal("confirmation_required", result.Code);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateAsync_BindsOnlyAccessibleProject_AndPersistsOneAuditableReplaySafeFact()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDatabaseAsync(connection);
        await SeedAsync(db);
        var service = new ProjectContextApplicationService(new ProjectContextStoreAdapter(db));
        var command = new ActivateProjectContextCommand(
            "user-1",
            "session-1",
            "project-1",
            "activate-1",
            0,
            SourceUserMessageId: "message-9");

        var catalog = await service.ListAccessibleProjectsAsync("user-1");
        var first = await service.ActivateAsync(command);
        var replay = await service.ActivateAsync(command);

        Assert.Equal(2, catalog.Count);
        var item = Assert.Single(catalog, candidate => candidate.ProjectId == "project-1");
        Assert.Equal("project-1", item.ProjectId);
        Assert.Equal("我的项目", item.Title);
        Assert.Null(item.Description);
        Assert.True(first.Succeeded);
        Assert.Equal("activated", first.Code);
        Assert.Equal("project-1", first.ProjectId);
        Assert.Equal(1, first.BindingVersion);
        Assert.Equal(first, replay);

        db.ChangeTracker.Clear();
        var session = await db.AgentSessions.SingleAsync(item => item.Id == "session-1");
        var activation = await db.ProjectContextActivations.SingleAsync();
        Assert.Equal("project-1", session.ProjectId);
        Assert.Equal(1, session.BindingVersion);
        Assert.Equal("message-9", activation.SourceUserMessageId);
        Assert.Equal("activate-1", activation.IdempotencyKey);
        Assert.Equal(0, activation.PreviousBindingVersion);
        Assert.Equal(1, activation.BindingVersion);
        Assert.Empty(await db.CreativeGoals.ToListAsync());
        Assert.Empty(await db.BookProductions.ToListAsync());
        Assert.Empty(await db.KernelTasks.ToListAsync());
        Assert.Empty(await db.CandidateAcceptances.ToListAsync());
        Assert.Empty(await db.BranchMergeRecords.ToListAsync());
    }

    [Fact]
    public async Task ActivateAsync_RejectsUnauthorizedProjectWithoutChangingConversation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDatabaseAsync(connection);
        await SeedAsync(db);
        var service = new ProjectContextApplicationService(new ProjectContextStoreAdapter(db));

        var result = await service.ActivateAsync(new ActivateProjectContextCommand(
            "user-1",
            "session-1",
            "project-2",
            "activate-unauthorized",
            0,
            ConfirmationActionId: "web-action-1"));

        Assert.False(result.Succeeded);
        Assert.True(result.Recoverable);
        Assert.Equal("project_unavailable", result.Code);
        db.ChangeTracker.Clear();
        var session = await db.AgentSessions.SingleAsync(item => item.Id == "session-1");
        Assert.Null(session.ProjectId);
        Assert.Equal(0, session.BindingVersion);
        Assert.Empty(await db.ProjectContextActivations.ToListAsync());
    }

    [Fact]
    public async Task ActivateAsync_RejectsStaleBindingVersionAndCrossProjectSwitch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDatabaseAsync(connection);
        await SeedAsync(db);
        var service = new ProjectContextApplicationService(new ProjectContextStoreAdapter(db));

        var stale = await service.ActivateAsync(new ActivateProjectContextCommand(
            "user-1",
            "session-1",
            "project-1",
            "activate-stale",
            7,
            ConfirmationActionId: "web-action-stale"));
        var activated = await service.ActivateAsync(new ActivateProjectContextCommand(
            "user-1",
            "session-1",
            "project-1",
            "activate-first",
            0,
            ConfirmationActionId: "web-action-first"));
        var switched = await service.ActivateAsync(new ActivateProjectContextCommand(
            "user-1",
            "session-1",
            "project-3",
            "activate-switch",
            1,
            ConfirmationActionId: "web-action-switch"));

        Assert.Equal("version_conflict", stale.Code);
        Assert.True(activated.Succeeded);
        Assert.Equal("switch_not_supported", switched.Code);
        Assert.True(switched.Recoverable);
        db.ChangeTracker.Clear();
        var session = await db.AgentSessions.SingleAsync(item => item.Id == "session-1");
        Assert.Equal("project-1", session.ProjectId);
        Assert.Equal(1, session.BindingVersion);
        Assert.Single(await db.ProjectContextActivations.ToListAsync());
    }

    [Fact]
    public void BindingVersion_IsAnOptimisticConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var db = new NovelAgentDbContext(options);

        var property = db.Model.FindEntityType(typeof(AgentSession))!
            .FindProperty(nameof(AgentSession.BindingVersion));

        Assert.NotNull(property);
        Assert.True(property!.IsConcurrencyToken);
    }

    private static async Task<NovelAgentDbContext> CreateDatabaseAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static async Task SeedAsync(NovelAgentDbContext db)
    {
        var user1 = User("user-1");
        var user2 = User("user-2");
        db.Users.AddRange(user1, user2);
        db.NovelProjects.AddRange(
            Project("project-1", user1, "我的项目"),
            Project("project-2", user2, "别人的项目"),
            Project("project-3", user1, "另一个项目"));
        db.AgentSessions.Add(new AgentSession
        {
            Id = "session-1",
            UserId = user1.Id,
            User = user1,
            Title = "新会话",
            SessionData = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static User User(string id) => new()
    {
        Id = id,
        Username = id,
        Email = $"{id}@example.com",
        PasswordHash = "hash",
        Role = "user"
    };

    private static NovelProject Project(string id, User user, string title) => new()
    {
        Id = id,
        UserId = user.Id,
        User = user,
        Title = title,
        Status = "draft",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

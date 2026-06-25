using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using Xunit;

namespace Tests.Unit.Services.AgentSessions;

public class AgentSessionServiceTests
{
    [Fact]
    public async Task GetOrCreateSessionAsync_ReturnsFrontendContractFields()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var response = await service.GetOrCreateSessionAsync(
            null,
            "user-1",
            "project-1",
            idempotencyKey: null,
            CancellationToken.None);

        Assert.Equal("project-1", response.ActiveProjectId);
        Assert.Null(response.ActiveRunId);
        Assert.Empty(response.Messages);
        Assert.NotNull(response.Memory);
        Assert.Equal(0, response.MessageCount);
        Assert.DoesNotContain(
            response.GetType().GetProperties().Select(p => p.Name),
            name => string.Equals(name, "SessionData", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_WithSameIdempotencyKey_ReturnsExistingSession()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var first = await service.GetOrCreateSessionAsync(
            null,
            "user-1",
            "project-1",
            idempotencyKey: "session-create-key-001",
            CancellationToken.None);
        var second = await service.GetOrCreateSessionAsync(
            null,
            "user-1",
            "project-1",
            idempotencyKey: "session-create-key-001",
            CancellationToken.None);

        Assert.Equal(first.SessionId, second.SessionId);
        var session = await db.AgentSessions.SingleAsync();
        Assert.Equal("session-create-key-001", session.IdempotencyKey);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_WithDifferentIdempotencyKeys_CreatesDifferentSessions()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var first = await service.GetOrCreateSessionAsync(
            null,
            "user-1",
            "project-1",
            idempotencyKey: "session-create-key-001",
            CancellationToken.None);
        var second = await service.GetOrCreateSessionAsync(
            null,
            "user-1",
            "project-1",
            idempotencyKey: "session-create-key-002",
            CancellationToken.None);

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(2, await db.AgentSessions.CountAsync());
    }

    [Fact]
    public async Task ListUserSessionsAsync_ReturnsActiveProjectAndMessageCount()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = "session-1",
            UserId = "user-1",
            ProjectId = "project-1",
            Title = "会话",
            SessionData = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.AgentChatTurns.Add(new AgentChatTurn
        {
            Id = "turn-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            TurnIndex = 1,
            Role = "user",
            Content = "hello"
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var response = await service.ListUserSessionsAsync("user-1", isAdmin: false, cancellationToken: CancellationToken.None);

        Assert.Single(response);
        Assert.Equal("project-1", response[0].ActiveProjectId);
        Assert.Equal(1, response[0].MessageCount);
    }

    [Fact]
    public async Task GetSessionByIdAsync_ReturnsStableTurnIdentityForFrontendKeys()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = "session-1",
            UserId = "user-1",
            ProjectId = "project-1",
            Title = "会话",
            SessionData = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.AgentChatTurns.AddRange(
            new AgentChatTurn
            {
                Id = "turn-user-1",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                TurnIndex = 1,
                Role = "user",
                Content = "继续第二章",
                CreatedAt = new DateTime(2026, 6, 24, 14, 0, 0, DateTimeKind.Utc)
            },
            new AgentChatTurn
            {
                Id = "turn-agent-1",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                TurnIndex = 2,
                Role = "assistant",
                Content = "第二章正在处理",
                CreatedAt = new DateTime(2026, 6, 24, 14, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var response = await service.GetSessionByIdAsync(
            "session-1",
            "user-1",
            isAdmin: false,
            CancellationToken.None);

        Assert.Collection(
            response.Messages,
            first =>
            {
                Assert.Equal("turn-user-1", first.TurnId);
                Assert.Equal(1, first.TurnIndex);
            },
            second =>
            {
                Assert.Equal("turn-agent-1", second.TurnId);
                Assert.Equal(2, second.TurnIndex);
            });
    }

    [Fact]
    public async Task ListUserSessionsAsync_AdminReturnsOnlyOwnAgentSessions()
    {
        await using var db = CreateDb();
        db.AgentSessions.AddRange(
            new AgentSession
            {
                Id = "admin-session",
                UserId = "admin-user",
                ProjectId = null,
                Title = "管理员自己的会话",
                SessionData = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new AgentSession
            {
                Id = "other-session",
                UserId = "other-user",
                ProjectId = null,
                Title = "其他用户的会话",
                SessionData = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var response = await service.ListUserSessionsAsync("admin-user", isAdmin: true, cancellationToken: CancellationToken.None);

        var session = Assert.Single(response);
        Assert.Equal("admin-session", session.SessionId);
    }

    [Fact]
    public async Task GetSessionByIdAsync_AdminCannotReadOtherUsersAgentSession()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = "other-session",
            UserId = "other-user",
            ProjectId = null,
            Title = "其他用户的会话",
            SessionData = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetSessionByIdAsync("other-session", "admin-user", isAdmin: true, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateSessionAsync_AdminCannotUpdateOtherUsersAgentSession()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = "other-session",
            UserId = "other-user",
            ProjectId = null,
            Title = "其他用户的会话",
            SessionData = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateSessionAsync(
                "other-session",
                new UpdateAgentSessionRequest { Title = "不应该成功" },
                "admin-user",
                isAdmin: true,
                CancellationToken.None));

        var otherSession = await db.AgentSessions.SingleAsync(s => s.Id == "other-session");
        Assert.Equal("其他用户的会话", otherSession.Title);
    }

    [Fact]
    public async Task DeleteSessionAsync_AdminCannotDeleteOtherUsersAgentSession()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = "other-session",
            UserId = "other-user",
            ProjectId = null,
            Title = "其他用户的会话",
            SessionData = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.DeleteSessionAsync("other-session", "admin-user", isAdmin: true, CancellationToken.None));

        Assert.True(await db.AgentSessions.AnyAsync(s => s.Id == "other-session"));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static AgentSessionService CreateService(NovelAgentDbContext db) =>
        new(db, new AgentRuntimeRunService(db), NullLogger<AgentSessionService>.Instance);
}

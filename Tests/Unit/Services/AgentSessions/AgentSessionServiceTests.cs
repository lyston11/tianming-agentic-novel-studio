using Microsoft.Data.Sqlite;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Models.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using Xunit;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Domain.Goals;
namespace Tests.Unit.Services.AgentSessions;

public class AgentSessionServiceTests
{
    [Fact]
    public async Task CreateUnboundSessionAsync_ReturnsFrontendContractFieldsAndPersistsNoProjectBinding()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var response = await service.CreateUnboundSessionAsync(
            "user-1",
            idempotencyKey: null,
            CancellationToken.None);

        Assert.Equal(string.Empty, response.ActiveProjectId);
        Assert.Null(response.ActiveRunId);
        Assert.Empty(response.Messages);
        Assert.NotNull(response.Memory);
        Assert.Equal(0, response.MessageCount);
        var session = await db.AgentSessions.SingleAsync();
        Assert.Null(session.ProjectId);
        Assert.DoesNotContain(
            response.GetType().GetProperties().Select(p => p.Name),
            name => string.Equals(name, "SessionData", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateUnboundSessionAsync_WithSameIdempotencyKey_ReturnsExistingSession()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var first = await service.CreateUnboundSessionAsync(
            "user-1",
            idempotencyKey: "session-create-key-001",
            CancellationToken.None);
        var second = await service.CreateUnboundSessionAsync(
            "user-1",
            idempotencyKey: "session-create-key-001",
            CancellationToken.None);

        Assert.Equal(first.SessionId, second.SessionId);
        var session = await db.AgentSessions.SingleAsync();
        Assert.Equal("session-create-key-001", session.IdempotencyKey);
        Assert.Null(session.ProjectId);
    }

    [Fact]
    public async Task CreateUnboundSessionAsync_WhenCompetingInsertWins_ReturnsWinner()
    {
        var connectionString = $"Data Source=session-race-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var setupOptions = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(keepAlive)
            .Options;
        await using (var setup = new NovelAgentDbContext(setupOptions))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        const string idempotencyKey = "session-create-race-001";
        const string winningSessionId = "winning-session";
        var competitorOptions = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var interceptor = new BeforeFirstSaveInterceptor(async cancellationToken =>
        {
            await using var competitor = new NovelAgentDbContext(competitorOptions);
            competitor.AgentSessions.Add(new AgentSession
            {
                Id = winningSessionId,
                UserId = "user-1",
                ProjectId = null,
                IdempotencyKey = idempotencyKey,
                Title = "新会话"
            });
            await competitor.SaveChangesAsync(cancellationToken);
        });
        var losingOptions = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var losingDb = new NovelAgentDbContext(losingOptions);
        var service = CreateService(losingDb);

        var response = await service.CreateUnboundSessionAsync(
            "user-1",
            idempotencyKey,
            CancellationToken.None);

        Assert.Equal(winningSessionId, response.SessionId);
        var session = await losingDb.AgentSessions.SingleAsync();
        Assert.Null(session.ProjectId);
    }

    [Fact]
    public async Task CreateUnboundSessionAsync_WithDifferentIdempotencyKeys_CreatesDifferentSessions()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var first = await service.CreateUnboundSessionAsync(
            "user-1",
            idempotencyKey: "session-create-key-001",
            CancellationToken.None);
        var second = await service.CreateUnboundSessionAsync(
            "user-1",
            idempotencyKey: "session-create-key-002",
            CancellationToken.None);

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(2, await db.AgentSessions.CountAsync());
        Assert.All(await db.AgentSessions.ToListAsync(), session => Assert.Null(session.ProjectId));
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
        await db.SaveChangesAsync();
        var service = CreateService(db, new ConversationRuntimeMessageRecord(
            "turn-1",
            UserRecord("hello"),
            DateTimeOffset.UtcNow));

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
        await db.SaveChangesAsync();
        var createdAt = new DateTimeOffset(2026, 6, 24, 14, 0, 0, TimeSpan.Zero);
        var service = CreateService(
            db,
            new ConversationRuntimeMessageRecord(
                "turn-user-1",
                new ConversationRuntimeMessage("user", "继续第二章"),
                createdAt),
            new ConversationRuntimeMessageRecord(
                "turn-agent-1",
                AssistantEnvelope("第二章正在处理"),
                createdAt.AddSeconds(1)));

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
    public async Task GetSessionByIdAsync_ReplaysDurableTurnsWithoutLegacyKnowledgeContext()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = "session-knowledge",
            UserId = "user-1",
            ProjectId = "project-1",
            Title = "知识会话",
            SessionData = "{}"
        });
        await db.SaveChangesAsync();
        var service = CreateService(
            db,
            new ConversationRuntimeMessageRecord(
                "turn-assistant-1",
                AssistantEnvelope("已读取人物设定。"),
                DateTimeOffset.UtcNow));

        var response = await service.GetSessionByIdAsync(
            "session-knowledge",
            "user-1",
            isAdmin: false,
            CancellationToken.None);

        var restored = Assert.Single(response.Messages);
        Assert.Equal("已读取人物设定。", restored.Content);
        Assert.Null(restored.Knowledge);
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

    private static AgentSessionService CreateService(
        NovelAgentDbContext db,
        params ConversationRuntimeMessageRecord[] records) =>
        new(db, new StubConversationStore(records), NullLogger<AgentSessionService>.Instance);

    private sealed class StubConversationStore(IReadOnlyList<ConversationRuntimeMessageRecord> records)
        : IConversationStore
    {
        public Task<ConversationTurnResult?> FindTurnResultAsync(
            string userId,
            string sessionId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConversationTurnResult?>(null);

        public Task SaveTurnAsync(
            string userId,
            string? projectId,
            string sessionId,
            string idempotencyKey,
            string userMessageId,
            string userMessage,
            string assistantMessageId,
            ConversationRuntimeResult runtimeResult,
            GoalProposal? proposal,
            ConversationTurnResult result,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ConversationRuntimeMessage>> ReadMessagesAsync(
            string userId,
            string sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConversationRuntimeMessage>>([]);

        public Task<IReadOnlyList<ConversationRuntimeMessageRecord>> ReadMessageRecordsAsync(
            string userId,
            string sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConversationRuntimeMessageRecord>>(records);

        public Task<GoalProposal?> GetProposalAsync(
            string userId,
            string proposalId,
            CancellationToken cancellationToken) =>
            Task.FromResult<GoalProposal?>(null);

        public Task UpdateProposalAsync(GoalProposal proposal, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static ConversationRuntimeMessage UserRecord(string content) =>
        new("user", content);

    private static ConversationRuntimeMessage AssistantEnvelope(string text) =>
        new(
            "assistant",
            JsonSerializer.Serialize(new[] { new { type = "text", text } }),
            CustomType: "pi.assistant.v1");

    private sealed class BeforeFirstSaveInterceptor(Func<CancellationToken, Task> action) : SaveChangesInterceptor
    {
        private int _invoked;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _invoked, 1) == 0)
            {
                await action(cancellationToken);
            }

            return result;
        }
    }

}

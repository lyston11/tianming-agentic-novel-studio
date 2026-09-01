using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Auth;
using Xunit;

namespace Tests.Unit.Services.AgentSessions;

public sealed class AgentChatIdempotencyServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ReplaysCompletedResponseWithoutExecutingDirectorTwice()
    {
        await using var db = CreateDb();
        db.Users.Add(UserRow());
        await db.SaveChangesAsync();
        var service = new AgentChatIdempotencyService(db, new FixedCurrentUser());
        var calls = 0;

        Task<AgentChatResponse> Execute()
        {
            calls++;
            return Task.FromResult(new AgentChatResponse("原始回复", ["继续"], "session-1", Phase: "goal_exploring"));
        }

        var first = await service.ExecuteAsync("session-1", "讨论目标", "message-1", Execute);
        var replay = await service.ExecuteAsync("session-1", "讨论目标", "message-1", Execute);

        Assert.Equal(1, calls);
        Assert.Equal(first.Reply, replay.Reply);
        Assert.Equal(first.SessionId, replay.SessionId);
        Assert.Equal(first.Phase, replay.Phase);
        Assert.Equal(first.Suggestions, replay.Suggestions);
        var receipt = Assert.Single(await db.AgentChatRequestReceipts.ToListAsync());
        Assert.Equal("completed", receipt.Status);
        Assert.Equal("session-1", receipt.ResolvedSessionId);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsDifferentPayloadForSameKey()
    {
        await using var db = CreateDb();
        db.Users.Add(UserRow());
        await db.SaveChangesAsync();
        var service = new AgentChatIdempotencyService(db, new FixedCurrentUser());
        await service.ExecuteAsync(
            "session-1",
            "第一条消息",
            "message-1",
            () => Task.FromResult(new AgentChatResponse("回复", [], "session-1")));

        await Assert.ThrowsAsync<AgentChatIdempotencyConflictException>(() => service.ExecuteAsync(
            "session-1",
            "另一条消息",
            "message-1",
            () => Task.FromResult(new AgentChatResponse("不应执行", [], "session-1"))));
    }

    [Fact]
    public async Task ExecuteAsync_TakesOverExpiredProcessingReceipt()
    {
        await using var db = CreateDb();
        db.Users.Add(UserRow());
        db.AgentChatRequestReceipts.Add(new AgentChatRequestReceipt
        {
            Id = "receipt-1",
            UserId = "user-1",
            RequestedSessionId = "session-1",
            CanonicalKey = "message-1",
            RequestHash = Sha256("讨论目标"),
            Status = "processing",
            LeaseOwner = "dead-worker",
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-20)
        });
        await db.SaveChangesAsync();
        var service = new AgentChatIdempotencyService(db, new FixedCurrentUser());
        var calls = 0;

        var response = await service.ExecuteAsync(
            "session-1",
            "讨论目标",
            "message-1",
            () =>
            {
                calls++;
                return Task.FromResult(new AgentChatResponse("恢复后的回复", [], "session-1"));
            });

        Assert.Equal(1, calls);
        Assert.Equal("恢复后的回复", response.Reply);
        var receipt = await db.AgentChatRequestReceipts.SingleAsync();
        Assert.Equal("completed", receipt.Status);
        Assert.Null(receipt.LeaseOwner);
        Assert.Null(receipt.LeaseExpiresAt);
    }

    private static NovelAgentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static User UserRow() => new()
    {
        Id = "user-1",
        Username = "user-1",
        Email = "user-1@example.test",
        PasswordHash = "hash",
        Role = "author"
    };

    private static string Sha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public string GetUserId() => "user-1";
        public string GetUsername() => "user-1";
        public string GetEmail() => "user-1@example.test";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => "user-1";
    }
}

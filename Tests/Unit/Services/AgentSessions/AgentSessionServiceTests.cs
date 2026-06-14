using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using Xunit;

namespace Tests.Unit.Services.AgentSessions;

public class AgentSessionServiceTests
{
    [Fact]
    public async Task GetOrCreateSessionAsync_ReturnsFrontendContractFields()
    {
        await using var db = CreateDb();
        var service = new AgentSessionService(db, NullLogger<AgentSessionService>.Instance);

        var response = await service.GetOrCreateSessionAsync(
            null,
            "user-1",
            "project-1",
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
        var service = new AgentSessionService(db, NullLogger<AgentSessionService>.Instance);

        var response = await service.ListUserSessionsAsync("user-1", isAdmin: false, cancellationToken: CancellationToken.None);

        Assert.Single(response);
        Assert.Equal("project-1", response[0].ActiveProjectId);
        Assert.Equal(1, response[0].MessageCount);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}

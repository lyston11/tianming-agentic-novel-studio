using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentApplication;
using Xunit;

namespace Tests.Unit.Services.AgentApplication;

public sealed class ConversationSessionBindingReaderAdapterTests
{
    [Fact]
    public async Task GetBinding_returns_explicit_unbound_and_bound_contexts()
    {
        await using var db = CreateDb();
        db.AgentSessions.AddRange(
            Session("session-unbound", "user-1", projectId: null),
            Session("session-bound", "user-1", "project-1"));
        db.NovelProjects.Add(new NovelProject { Id = "project-1", UserId = "user-1", Title = "Owned" });
        await db.SaveChangesAsync();
        var reader = new ConversationSessionBindingReaderAdapter(db);

        var unbound = await reader.GetBindingAsync("user-1", "session-unbound", CancellationToken.None);
        var bound = await reader.GetBindingAsync("user-1", "session-bound", CancellationToken.None);

        Assert.IsType<UnboundConversationBinding>(unbound);
        Assert.Equal("project-1", Assert.IsType<BoundConversationBinding>(bound).ProjectId);
    }

    [Theory]
    [InlineData("missing", "user-1")]
    [InlineData("session-owned-by-other-user", "user-1")]
    public async Task GetBinding_rejects_missing_or_unowned_conversations(
        string sessionId,
        string userId)
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(Session("session-owned-by-other-user", "user-2", projectId: null));
        await db.SaveChangesAsync();
        var reader = new ConversationSessionBindingReaderAdapter(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            reader.GetBindingAsync(userId, sessionId, CancellationToken.None));
    }

    [Theory]
    [InlineData("missing-project")]
    [InlineData("project-owned-by-other-user")]
    public async Task GetBinding_rejects_missing_or_unowned_bound_projects(string projectId)
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(Session("session-bound", "user-1", projectId));
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-owned-by-other-user",
            UserId = "user-2",
            Title = "Other user project"
        });
        await db.SaveChangesAsync();
        var reader = new ConversationSessionBindingReaderAdapter(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            reader.GetBindingAsync("user-1", "session-bound", CancellationToken.None));
    }

    [Fact]
    public async Task GetBinding_rejects_archived_conversations()
    {
        await using var db = CreateDb();
        var archived = Session("session-archived", "user-1", projectId: null);
        archived.IsArchived = true;
        db.AgentSessions.Add(archived);
        await db.SaveChangesAsync();
        var reader = new ConversationSessionBindingReaderAdapter(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.GetBindingAsync("user-1", "session-archived", CancellationToken.None));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static AgentSession Session(string id, string userId, string? projectId) => new()
    {
        Id = id,
        UserId = userId,
        ProjectId = projectId,
        Title = id
    };
}

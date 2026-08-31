using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentApplication;
using Xunit;

namespace Tests.Unit.Services.AgentApplication;

public sealed class NovelAgentResourceAuthorizerTests
{
    [Fact]
    public async Task RequireProject_rejects_a_project_owned_by_another_user()
    {
        await using var db = CreateDb();
        db.NovelProjects.Add(Project("project-1", "user-2"));
        await db.SaveChangesAsync();
        var authorizer = new NovelAgentResourceAuthorizer(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            authorizer.RequireProjectAsync("user-1", "project-1", CancellationToken.None));
    }

    [Fact]
    public async Task RequireConversation_checks_both_user_and_project_scope()
    {
        await using var db = CreateDb();
        db.NovelProjects.AddRange(Project("project-1", "user-1"), Project("project-2", "user-1"));
        db.AgentSessions.Add(new AgentSession
        {
            Id = "session-1",
            UserId = "user-1",
            ProjectId = "project-1"
        });
        await db.SaveChangesAsync();
        var authorizer = new NovelAgentResourceAuthorizer(db);

        await authorizer.RequireConversationAsync(
            "user-1", "session-1", "project-1", CancellationToken.None);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            authorizer.RequireConversationAsync(
                "user-1", "session-1", "project-2", CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            authorizer.RequireConversationAsync(
                "user-2", "session-1", projectId: null, CancellationToken.None));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static NovelProject Project(string id, string userId) => new()
    {
        Id = id,
        UserId = userId,
        Title = id,
        Status = "draft"
    };
}

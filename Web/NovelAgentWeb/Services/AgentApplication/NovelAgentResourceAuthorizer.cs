using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.AgentApplication;

public interface INovelAgentResourceAuthorizer
{
    Task RequireProjectAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken);

    Task RequireConversationAsync(
        string userId,
        string sessionId,
        string? projectId,
        CancellationToken cancellationToken);
}

public sealed class NovelAgentResourceAuthorizer(NovelAgentDbContext db) : INovelAgentResourceAuthorizer
{
    public async Task RequireProjectAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken)
    {
        var exists = await db.NovelProjects.AsNoTracking().AnyAsync(project =>
            project.Id == projectId && project.UserId == userId,
            cancellationToken);
        if (!exists)
            throw new KeyNotFoundException("Project was not found.");
    }

    public async Task RequireConversationAsync(
        string userId,
        string sessionId,
        string? projectId,
        CancellationToken cancellationToken)
    {
        var exists = await db.AgentSessions.AsNoTracking().AnyAsync(session =>
            session.Id == sessionId &&
            session.UserId == userId &&
            (projectId == null || session.ProjectId == projectId),
            cancellationToken);
        if (!exists)
            throw new KeyNotFoundException("Conversation was not found.");
    }
}

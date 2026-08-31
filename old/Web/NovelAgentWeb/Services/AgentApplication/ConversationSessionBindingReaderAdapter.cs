using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.AgentApplication;

public sealed class ConversationSessionBindingReaderAdapter(NovelAgentDbContext db)
    : IConversationSessionBindingReader
{
    public async Task<ConversationBinding> GetBindingAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var session = await db.AgentSessions
            .AsNoTracking()
            .Where(item => item.Id == sessionId && item.UserId == userId)
            .Select(item => new
            {
                item.ProjectId,
                item.IsArchived,
                ProjectAccessible = item.ProjectId == null || db.NovelProjects.Any(project =>
                    project.Id == item.ProjectId && project.UserId == userId)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (session is null)
            throw new KeyNotFoundException("Conversation was not found.");
        if (session.IsArchived)
            throw new InvalidOperationException("Archived conversations cannot accept new turns.");
        if (!session.ProjectAccessible)
            throw new KeyNotFoundException("The bound project is unavailable to the current user.");
        if (session.ProjectId is null)
            return new UnboundConversationBinding();
        if (string.IsNullOrWhiteSpace(session.ProjectId))
            throw new InvalidOperationException("The conversation has an invalid project binding.");

        return new BoundConversationBinding(session.ProjectId);
    }
}

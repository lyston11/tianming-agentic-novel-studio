using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Infrastructure.Persistence;

namespace Tianming.NovelAgent.Infrastructure.Conversation;

public sealed class EfMafSessionCheckpointStore(
    AgentControlDbContext db) : IMafSessionCheckpointStore
{
    public Task<string?> LoadAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken) =>
        db.ConversationRuntimeCheckpoints
            .Where(x => x.UserId == userId && x.SessionId == sessionId && x.Runtime == "maf")
            .Select(x => x.CheckpointJson)
            .SingleOrDefaultAsync(cancellationToken);
}

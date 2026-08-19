using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Infrastructure.Persistence;

namespace Tianming.NovelAgent.Infrastructure.Conversation;

public sealed class EfMafSessionCheckpointStore(
    AgentControlDbContext db,
    IClock clock) : IMafSessionCheckpointStore
{
    public Task<string?> LoadAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken) =>
        db.ConversationRuntimeCheckpoints
            .Where(x => x.UserId == userId && x.SessionId == sessionId && x.Runtime == "maf")
            .Select(x => x.CheckpointJson)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task SaveAsync(
        string userId,
        string projectId,
        string sessionId,
        string checkpointJson,
        CancellationToken cancellationToken)
    {
        var record = await db.ConversationRuntimeCheckpoints.SingleOrDefaultAsync(
            x => x.UserId == userId && x.SessionId == sessionId && x.Runtime == "maf",
            cancellationToken);
        if (record is null)
        {
            record = new ConversationRuntimeCheckpointRecord
            {
                Id = $"{userId}:{sessionId}:maf",
                UserId = userId,
                ProjectId = projectId,
                SessionId = sessionId,
                Runtime = "maf",
                Version = 1
            };
            db.ConversationRuntimeCheckpoints.Add(record);
        }
        else
        {
            record.Version++;
        }
        record.CheckpointJson = checkpointJson;
        record.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}

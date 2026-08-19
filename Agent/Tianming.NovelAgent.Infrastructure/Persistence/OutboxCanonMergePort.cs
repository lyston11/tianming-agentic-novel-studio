using Tianming.NovelAgent.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Tianming.NovelAgent.Infrastructure.Persistence;

public sealed class OutboxCanonMergePort(
    AgentControlDbContext db,
    IIdGenerator ids,
    IClock clock) : ICanonMergePort
{
    public async Task<CanonMergeRequest?> FindByIdempotencyKeyAsync(
        string userId,
        string productionId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var key = BuildIdempotencyKey(userId, productionId, idempotencyKey);
        var payload = await db.OutboxEvents.AsNoTracking()
            .Where(x => x.UserId == userId && x.IdempotencyKey == key)
            .Select(x => x.PayloadJson)
            .SingleOrDefaultAsync(cancellationToken);
        return payload is null ? null : AgentJson.Deserialize<CanonMergeRequest>(payload);
    }

    public async Task<CanonMergeRequest?> GetAsync(
        string userId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var payloads = await db.OutboxEvents.AsNoTracking()
            .Where(x => x.UserId == userId && x.EventType == "canon_merge_requested")
            .Select(x => x.PayloadJson)
            .ToListAsync(cancellationToken);
        return payloads.Select(AgentJson.Deserialize<CanonMergeRequest>)
            .SingleOrDefault(x => x.RequestId == requestId);
    }

    public Task RequestMergeAsync(CanonMergeRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        db.OutboxEvents.Add(new OutboxEventRecord
        {
            Id = ids.NewId(),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            EventType = "canon_merge_requested",
            AggregateType = "Production",
            AggregateId = request.ProductionId,
            IdempotencyKey = BuildIdempotencyKey(request.UserId, request.ProductionId, request.IdempotencyKey),
            PayloadJson = AgentJson.Serialize(request),
            Status = "pending",
            CreatedAt = now,
            UpdatedAt = now
        });
        return Task.CompletedTask;
    }

    private static string BuildIdempotencyKey(string userId, string productionId, string idempotencyKey) =>
        $"canon-merge:{userId}:{productionId}:{idempotencyKey}";
}

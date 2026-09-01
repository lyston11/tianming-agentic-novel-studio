using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionOutboxAdminService : IProductionOutboxAdminService
{
    private readonly NovelAgentDbContext _db;
    private readonly IProductionOutboxDispatcher _dispatcher;

    public ProductionOutboxAdminService(
        NovelAgentDbContext db,
        IProductionOutboxDispatcher dispatcher)
    {
        _db = db;
        _dispatcher = dispatcher;
    }

    public async Task<OutboxAdminListResponse> ListAsync(
        string? projectId,
        string? status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = _db.OutboxEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(projectId))
            query = query.Where(evt => evt.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(evt => evt.Status == status);

        var items = await query
            .OrderByDescending(evt => evt.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(evt => new OutboxAdminItem
            {
                Id = evt.Id,
                UserId = evt.UserId,
                ProjectId = evt.ProjectId ?? string.Empty,
                RuntimeRunId = evt.RuntimeRunId ?? string.Empty,
                EventType = evt.EventType,
                AggregateType = evt.AggregateType,
                AggregateId = evt.AggregateId,
                Status = evt.Status,
                Attempts = evt.Attempts,
                LastError = evt.LastError ?? string.Empty,
                NextAttemptAt = evt.NextAttemptAt,
                CompletedAt = evt.CompletedAt,
                CreatedAt = evt.CreatedAt,
                UpdatedAt = evt.UpdatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OutboxAdminListResponse
        {
            Count = items.Count,
            Items = items
        };
    }

    public async Task<OutboxAdminRetryResponse> RetryAsync(
        string eventId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var evt = await _db.OutboxEvents
            .SingleOrDefaultAsync(item => item.Id == eventId, cancellationToken)
            .ConfigureAwait(false);
        if (evt == null)
            throw new KeyNotFoundException($"Outbox event {eventId} not found.");

        var normalizedKey = Normalize(idempotencyKey);
        if (!string.IsNullOrWhiteSpace(normalizedKey) &&
            HasAdminRetryIdempotencyKey(evt.PayloadJson, normalizedKey))
        {
            return await BuildRetryResponseAsync(evt, dispatchAttempted: 0, cancellationToken)
                .ConfigureAwait(false);
        }

        evt.Status = "pending";
        evt.Attempts = 0;
        evt.LastError = null;
        evt.NextAttemptAt = null;
        evt.CompletedAt = null;
        if (!string.IsNullOrWhiteSpace(normalizedKey))
            evt.PayloadJson = AddAdminRetryIdempotencyKey(evt.PayloadJson, normalizedKey);
        evt.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var dispatched = await _dispatcher
            .DispatchPendingAsync(maxItems: 1, cancellationToken)
            .ConfigureAwait(false);

        await _db.Entry(evt).ReloadAsync(cancellationToken).ConfigureAwait(false);

        return await BuildRetryResponseAsync(evt, dispatched, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OutboxAdminRetryResponse> BuildRetryResponseAsync(
        Data.Entities.OutboxEvent evt,
        int dispatchAttempted,
        CancellationToken cancellationToken)
    {
        var pendingCount = await _db.OutboxEvents
            .CountAsync(item => item.Status == "pending" || item.Status == "retryable_failed", cancellationToken)
            .ConfigureAwait(false);

        return new OutboxAdminRetryResponse
        {
            EventId = evt.Id,
            Status = evt.Status,
            Attempts = evt.Attempts,
            LastError = evt.LastError ?? string.Empty,
            NextAttemptAt = evt.NextAttemptAt,
            DispatchAttempted = dispatchAttempted,
            PendingCount = pendingCount
        };
    }

    private static string Normalize(string? value) =>
        value?.Trim() ?? string.Empty;

    private static bool HasAdminRetryIdempotencyKey(string? payloadJson, string idempotencyKey)
    {
        var root = ParsePayload(payloadJson);
        var keys = root["adminRetryIdempotencyKeys"] as JsonArray;
        return keys != null && keys.Any(item =>
            string.Equals(item?.GetValue<string>(), idempotencyKey, StringComparison.OrdinalIgnoreCase));
    }

    private static string AddAdminRetryIdempotencyKey(string? payloadJson, string idempotencyKey)
    {
        var root = ParsePayload(payloadJson);
        var keys = root["adminRetryIdempotencyKeys"] as JsonArray;
        if (keys == null)
        {
            keys = new JsonArray();
            root["adminRetryIdempotencyKeys"] = keys;
        }

        if (!keys.Any(item =>
                string.Equals(item?.GetValue<string>(), idempotencyKey, StringComparison.OrdinalIgnoreCase)))
        {
            keys.Add(idempotencyKey);
        }

        return root.ToJsonString();
    }

    private static JsonObject ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(payloadJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject
            {
                ["rawPayload"] = payloadJson
            };
        }
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public sealed class AgentInterruptService : IAgentInterruptService
{
    private static readonly TimeSpan InterruptQueueCacheTtl = TimeSpan.FromMinutes(30);
    private readonly NovelAgentDbContext _db;
    private readonly IDistributedCacheService? _redis;
    private readonly IAgentRuntimeRunService? _runtimeRuns;

    public AgentInterruptService(
        NovelAgentDbContext db,
        IDistributedCacheService? redis = null,
        IAgentRuntimeRunService? runtimeRuns = null)
    {
        _db = db;
        _redis = redis;
        _runtimeRuns = runtimeRuns;
    }

    public async Task<AgentInterrupt> AddAsync(CreateAgentInterruptRequest request, CancellationToken ct = default)
    {
        var interrupt = new AgentInterrupt
        {
            Id = Guid.NewGuid().ToString("N"),
            RuntimeRunId = request.RuntimeRunId,
            UserId = request.UserId,
            SessionId = request.SessionId,
            ProjectId = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId,
            Kind = string.IsNullOrWhiteSpace(request.Kind) ? "freeform" : request.Kind,
            Status = AgentInterruptStatus.Pending,
            Priority = request.Priority,
            Message = request.Message.Trim(),
            DecisionJson = "{}",
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentInterrupts.Add(interrupt);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (IsCancelInterrupt(interrupt.Kind) && _runtimeRuns != null)
        {
            var run = await _runtimeRuns.TryGetAsync(interrupt.RuntimeRunId, ct).ConfigureAwait(false);
            if (run?.CancelRequested != true)
                await _runtimeRuns.RequestCancelAsync(interrupt.RuntimeRunId, ct).ConfigureAwait(false);
        }
        await CachePendingInterruptsAsync(interrupt.RuntimeRunId, ct).ConfigureAwait(false);
        return interrupt;
    }

    public async Task<IReadOnlyList<AgentInterrupt>> GetPendingAsync(string runtimeRunId, CancellationToken ct = default)
    {
        var pending = await QueryPendingAsync(runtimeRunId, ct).ConfigureAwait(false);
        await WritePendingSnapshotAsync(runtimeRunId, pending, ct).ConfigureAwait(false);
        return pending;
    }

    public async Task<AgentRuntimeInterruptQueueSnapshot?> TryGetPendingSnapshotAsync(
        string runtimeRunId,
        CancellationToken ct = default)
    {
        var key = BuildInterruptQueueKey(runtimeRunId);
        var snapshot = _redis == null
            ? null
            : await _redis.GetAsync<AgentRuntimeInterruptQueueSnapshot>(key, ct).ConfigureAwait(false);
        if (snapshot != null && string.Equals(snapshot.RuntimeRunId, runtimeRunId, StringComparison.Ordinal))
            return snapshot;

        var pending = await QueryPendingAsync(runtimeRunId, ct).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            if (_redis != null)
                await _redis.RemoveAsync(key, ct).ConfigureAwait(false);
            return null;
        }

        var rebuilt = BuildPendingSnapshot(runtimeRunId, pending);
        if (_redis != null)
            await _redis.SetAsync(key, rebuilt, InterruptQueueCacheTtl, ct).ConfigureAwait(false);
        return rebuilt;
    }

    public async Task<AgentInterrupt?> MarkConsumedAsync(string interruptId, object? decision, CancellationToken ct = default)
    {
        var interrupt = await _db.AgentInterrupts.FirstOrDefaultAsync(i => i.Id == interruptId, ct).ConfigureAwait(false);
        if (interrupt == null)
            return null;

        interrupt.Status = AgentInterruptStatus.Consumed;
        interrupt.DecisionJson = Serialize(decision);
        interrupt.ConsumedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await CachePendingInterruptsAsync(interrupt.RuntimeRunId, ct).ConfigureAwait(false);
        return interrupt;
    }

    public async Task<AgentInterrupt?> MarkRejectedAsync(string interruptId, string reason, CancellationToken ct = default)
    {
        var interrupt = await _db.AgentInterrupts.FirstOrDefaultAsync(i => i.Id == interruptId, ct).ConfigureAwait(false);
        if (interrupt == null)
            return null;

        interrupt.Status = AgentInterruptStatus.Rejected;
        interrupt.DecisionJson = Serialize(new { reason });
        interrupt.ConsumedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await CachePendingInterruptsAsync(interrupt.RuntimeRunId, ct).ConfigureAwait(false);
        return interrupt;
    }

    private async Task<List<AgentInterrupt>> QueryPendingAsync(string runtimeRunId, CancellationToken ct) =>
        await _db.AgentInterrupts
            .Where(i => i.RuntimeRunId == runtimeRunId && i.Status == AgentInterruptStatus.Pending)
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private async Task CachePendingInterruptsAsync(string runtimeRunId, CancellationToken ct)
    {
        if (_redis == null)
            return;

        var pending = await QueryPendingAsync(runtimeRunId, ct).ConfigureAwait(false);
        await WritePendingSnapshotAsync(runtimeRunId, pending, ct).ConfigureAwait(false);
    }

    private async Task WritePendingSnapshotAsync(
        string runtimeRunId,
        IReadOnlyList<AgentInterrupt> pending,
        CancellationToken ct)
    {
        if (_redis == null)
            return;

        var key = BuildInterruptQueueKey(runtimeRunId);
        if (pending.Count == 0)
        {
            await _redis.RemoveAsync(key, ct).ConfigureAwait(false);
            return;
        }

        await _redis.SetAsync(key, BuildPendingSnapshot(runtimeRunId, pending), InterruptQueueCacheTtl, ct)
            .ConfigureAwait(false);
    }

    private static AgentRuntimeInterruptQueueSnapshot BuildPendingSnapshot(
        string runtimeRunId,
        IReadOnlyList<AgentInterrupt> pending) =>
        new(
            RuntimeRunId: runtimeRunId,
            Pending: pending
                .Select(i => new AgentRuntimeInterruptCacheItem(
                    InterruptId: i.Id,
                    RuntimeRunId: i.RuntimeRunId,
                    UserId: i.UserId,
                    SessionId: i.SessionId,
                    ProjectId: i.ProjectId,
                    Kind: i.Kind,
                    Message: i.Message,
                    Priority: i.Priority,
                    CreatedAt: i.CreatedAt))
                .ToList(),
            PendingCount: pending.Count,
            UpdatedAt: DateTime.UtcNow);

    private static string BuildInterruptQueueKey(string runtimeRunId) =>
        $"agent_runtime:interrupts:{runtimeRunId}";

    private static bool IsCancelInterrupt(string? kind) =>
        string.Equals(kind?.Trim(), "cancel", StringComparison.OrdinalIgnoreCase);

    private static string Serialize(object? value) =>
        value == null
            ? "{}"
            : JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}

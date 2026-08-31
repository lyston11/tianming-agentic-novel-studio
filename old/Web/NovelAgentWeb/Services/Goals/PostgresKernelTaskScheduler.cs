using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tianming.NovelAgent.Infrastructure.Persistence;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.AgentApplication;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class PostgresKernelTaskScheduler : IKernelTaskScheduler
{
    private readonly AgentControlDbContext _db;
    private readonly AgentUserScope _userScope;
    private readonly IBackgroundClaimConnectionFactory _claimConnections;

    public PostgresKernelTaskScheduler(
        AgentControlDbContext db,
        AgentUserScope userScope,
        IBackgroundClaimConnectionFactory claimConnections)
    {
        _db = db;
        _userScope = userScope;
        _claimConnections = claimConnections;
    }

    public async Task<KernelTaskClaim?> ClaimNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID 不能为空。", nameof(workerId));
        var leaseSeconds = checked((int)Math.Ceiling(leaseDuration.TotalSeconds));
        if (leaseSeconds is < 5 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease 必须在 5 秒到 1 小时之间。");

        if (_db.Database.GetDbConnection() is not NpgsqlConnection)
            throw new InvalidOperationException("持久任务 claim 只支持 PostgreSQL。");
        await using var connection = await _claimConnections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM claim_kernel_task(@worker_id, @lease_seconds)";
        command.Parameters.Add(new NpgsqlParameter("worker_id", workerId.Trim()));
        command.Parameters.Add(new NpgsqlParameter("lease_seconds", leaseSeconds));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new KernelTaskClaim(
            reader.GetString(reader.GetOrdinal("task_id")),
            reader.GetString(reader.GetOrdinal("user_id")),
            reader.GetString(reader.GetOrdinal("project_id")),
            reader.GetString(reader.GetOrdinal("goal_id")),
            reader.GetString(reader.GetOrdinal("task_graph_version_id")),
            reader.IsDBNull(reader.GetOrdinal("branch_id"))
                ? null
                : reader.GetString(reader.GetOrdinal("branch_id")),
            reader.GetString(reader.GetOrdinal("kernel_name")),
            reader.GetString(reader.GetOrdinal("task_type")),
            reader.GetInt32(reader.GetOrdinal("attempt")),
            reader.GetString(reader.GetOrdinal("lease_owner")),
            reader.GetFieldValue<DateTime>(reader.GetOrdinal("lease_expires_at")));
    }

    public async Task CompleteAsync(
        KernelTaskClaim claim,
        IReadOnlyList<string> artifactIds,
        CancellationToken cancellationToken = default)
    {
        using var userScope = _userScope.Enter(claim.UserId);
        await using var transaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var updated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE kernel_tasks
                SET status = 'completed',
                    output_artifact_ids_json = {JsonSerializer.Serialize(artifactIds)}::jsonb,
                    lease_owner = NULL,
                    lease_expires_at = NULL,
                    completed_at = clock_timestamp(),
                    updated_at = clock_timestamp()
                WHERE id = {claim.TaskId}
                  AND user_id = {claim.UserId}
                  AND lease_owner = {claim.LeaseOwner}
                  AND status = 'running'
                """, cancellationToken);
            if (updated != 1)
                throw new InvalidOperationException("任务完成写入失败：lease 已失效或任务作用域不匹配。");

            _db.ChangeTracker.Clear();
            var graphTasks = await _db.KernelTasks
                .Where(task =>
                    task.UserId == claim.UserId &&
                    task.TaskGraphVersionId == claim.TaskGraphVersionId)
                .ToListAsync(cancellationToken);
            var byNodeId = graphTasks.ToDictionary(TaskNodeId, StringComparer.Ordinal);
            var acceptanceGates = new List<KernelTaskRecord>();
            foreach (var blocked in graphTasks.Where(task => task.Status == "blocked"))
            {
                var dependencies = JsonSerializer.Deserialize<string[]>(blocked.DependencyTaskIdsJson) ?? [];
                if (dependencies.All(dependency =>
                        byNodeId.TryGetValue(dependency, out var dependencyTask) &&
                        dependencyTask.Status is "completed" or "reused"))
                {
                    blocked.Status = blocked.TaskType == BookProductionWorkflow.AcceptanceGate ? "awaiting_user" : "ready";
                    blocked.UpdatedAt = DateTimeOffset.UtcNow;
                    if (blocked.Status == "awaiting_user")
                        acceptanceGates.Add(blocked);
                }
            }

            if (acceptanceGates.Count > 0)
            {
                var graph = await _db.TaskGraphs.AsNoTracking().SingleAsync(item =>
                    item.Id == claim.TaskGraphVersionId &&
                    item.UserId == claim.UserId &&
                    item.GoalId == claim.GoalId,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(graph.GoalRevisionId))
                {
                    foreach (var gate in acceptanceGates)
                        await AddAcceptanceGateOutboxAsync(claim, gate, cancellationToken);
                }
            }
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task AddAcceptanceGateOutboxAsync(
        KernelTaskClaim claim,
        KernelTaskRecord gate,
        CancellationToken cancellationToken)
    {
        var production = await (
            from batch in _db.ProductionBatches.AsNoTracking()
            join item in _db.BookProductions.AsNoTracking()
                on new { batch.UserId, Id = batch.BookProductionId }
                equals new { item.UserId, item.Id }
            where batch.UserId == claim.UserId
                && batch.ProjectId == claim.ProjectId
                && batch.GoalId == claim.GoalId
                && batch.TaskGraphVersionId == claim.TaskGraphVersionId
                && batch.CanonBranchId == gate.BranchId
            select item)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("AcceptanceGate 缺少对应的新 Production，不能可靠推进验收状态。");
        var payload = new AcceptanceGateReachedPayload(
            claim.UserId,
            claim.ProjectId,
            claim.GoalId,
            production.Id,
            claim.TaskGraphVersionId,
            gate.Id,
            gate.BranchId);
        var now = DateTimeOffset.UtcNow;
        _db.OutboxEvents.Add(new OutboxEventRecord
        {
            Id = $"novel-agent:acceptance-gate:{gate.Id}",
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            EventType = NovelAgentOutboxHandler.AcceptanceGateReachedEventType,
            AggregateType = "book_production",
            AggregateId = production.Id,
            IdempotencyKey = $"novel-agent:acceptance-gate:{gate.Id}",
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = "pending",
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    public async Task<bool> RenewAsync(
        KernelTaskClaim claim,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var leaseSeconds = checked((int)Math.Ceiling(leaseDuration.TotalSeconds));
        if (leaseSeconds is < 5 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease 必须在 5 秒到 1 小时之间。");

        using var userScope = _userScope.Enter(claim.UserId);
        var updated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE kernel_tasks
            SET lease_expires_at = clock_timestamp() + make_interval(secs => {leaseSeconds}),
                updated_at = clock_timestamp()
            WHERE id = {claim.TaskId}
              AND user_id = {claim.UserId}
              AND lease_owner = {claim.LeaseOwner}
              AND status = 'running'
            """, cancellationToken);
        return updated == 1;
    }

    public async Task FailAsync(
        KernelTaskClaim claim,
        string error,
        CancellationToken cancellationToken = default) =>
        await FailAsync(
            claim,
            new KernelTaskFailure(KernelTaskFailureCategory.NeedsDecision, error),
            cancellationToken).ConfigureAwait(false);

    public async Task FailAsync(
        KernelTaskClaim claim,
        KernelTaskFailure failure,
        CancellationToken cancellationToken = default)
    {
        using var userScope = _userScope.Enter(claim.UserId);
        var task = await _db.KernelTasks.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == claim.TaskId &&
            item.UserId == claim.UserId &&
            item.LeaseOwner == claim.LeaseOwner &&
            item.Status == "running",
            cancellationToken) ?? throw new InvalidOperationException(
                $"任务失败写入失败：lease 已失效或任务作用域不匹配。{failure.Message}");
        var decision = KernelTaskFailurePolicy.Decide(
            task.TaskType,
            task.Attempt,
            task.MaxAttempts,
            failure.Category);
        var status = decision.Disposition switch
        {
            KernelTaskFailureDisposition.Retry => "ready",
            KernelTaskFailureDisposition.AwaitingDecision => "awaiting_decision",
            KernelTaskFailureDisposition.FailGoal => "failed",
            _ => throw new ArgumentOutOfRangeException()
        };
        var nextAttemptAt = decision.Disposition == KernelTaskFailureDisposition.Retry
            ? DateTimeOffset.UtcNow.Add(decision.RetryAfter)
            : (DateTimeOffset?)null;
        var failureKind = failure.Category.ToString().ToLowerInvariant();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE kernel_tasks
            SET status = {status},
                lease_owner = NULL,
                lease_expires_at = NULL,
                next_attempt_at = {nextAttemptAt},
                last_error = {failure.Message},
                failure_kind = {failureKind},
                updated_at = clock_timestamp()
            WHERE id = {claim.TaskId}
              AND user_id = {claim.UserId}
              AND lease_owner = {claim.LeaseOwner}
              AND status = 'running'
            """, cancellationToken);
        if (updated != 1)
            throw new InvalidOperationException($"任务失败写入失败：{failure.Message}");

        if (decision.Disposition is not KernelTaskFailureDisposition.Retry)
            await ApplyTaskFailureAsync(claim, decision.Disposition, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ApplyTaskFailureAsync(
        KernelTaskClaim claim,
        KernelTaskFailureDisposition disposition,
        CancellationToken cancellationToken)
    {
        if (claim.TaskId.Contains(":manual-rework-", StringComparison.Ordinal))
            return;

        var goalStatus = disposition == KernelTaskFailureDisposition.AwaitingDecision
            ? "awaiting_decision"
            : "failed";
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE creative_goals
            SET status = {goalStatus},
                aggregate_version = aggregate_version + 1
            WHERE id = {claim.GoalId}
              AND user_id = {claim.UserId}
              AND status IN ('committed', 'running', 'resumed', 'awaiting_user', 'awaiting_next_batch')
            """, cancellationToken);

        var productionStatus = disposition == KernelTaskFailureDisposition.AwaitingDecision
            ? "blocked"
            : "failed";
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE book_productions
            SET status = {productionStatus},
                aggregate_version = aggregate_version + 1,
                updated_at = clock_timestamp()
            WHERE goal_id = {claim.GoalId}
              AND user_id = {claim.UserId}
              AND status IN ('running', 'resumed')
            """, cancellationToken);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE production_batches
            SET status = {productionStatus},
                updated_at = clock_timestamp()
            WHERE goal_id = {claim.GoalId}
              AND user_id = {claim.UserId}
              AND status IN ('planned', 'running', 'accepting')
            """, cancellationToken);
    }

    private static string TaskNodeId(KernelTaskRecord task)
    {
        var separator = task.Id.IndexOf(':');
        return separator < 0 ? task.Id : task.Id[(separator + 1)..];
    }

}

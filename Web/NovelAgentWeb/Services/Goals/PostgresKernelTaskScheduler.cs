using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class PostgresKernelTaskScheduler : IKernelTaskScheduler
{
    private readonly NovelAgentDbContext _db;
    private readonly IBackgroundClaimConnectionFactory _claimConnections;

    public PostgresKernelTaskScheduler(
        NovelAgentDbContext db,
        IBackgroundClaimConnectionFactory claimConnections)
    {
        _db = db;
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
        foreach (var blocked in graphTasks.Where(task => task.Status == "blocked"))
        {
            var dependencies = JsonSerializer.Deserialize<string[]>(blocked.DependencyTaskIdsJson) ?? [];
            if (dependencies.All(dependency =>
                    byNodeId.TryGetValue(dependency, out var dependencyTask) &&
                    dependencyTask.Status is "completed" or "reused"))
            {
                blocked.Status = blocked.TaskType == "UserAcceptance" ? "awaiting_user" : "ready";
                blocked.UpdatedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RenewAsync(
        KernelTaskClaim claim,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var leaseSeconds = checked((int)Math.Ceiling(leaseDuration.TotalSeconds));
        if (leaseSeconds is < 5 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease 必须在 5 秒到 1 小时之间。");

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
            ? DateTime.UtcNow.Add(decision.RetryAfter)
            : (DateTime?)null;
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
        {
            var goalStatus = decision.Disposition == KernelTaskFailureDisposition.AwaitingDecision
                ? "awaiting_decision"
                : "failed";
            var goalUpdated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE creative_goals
                SET status = {goalStatus},
                    aggregate_version = aggregate_version + 1
                WHERE id = {claim.GoalId}
                  AND user_id = {claim.UserId}
                  AND status IN ('committed', 'running', 'resumed')
                """, cancellationToken);
            if (goalUpdated != 1)
                throw new InvalidOperationException("Goal 失败终态写入失败或 Goal 已不再可执行。");
            var productionStatus = decision.Disposition == KernelTaskFailureDisposition.AwaitingDecision
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

        await transaction.CommitAsync(cancellationToken);
    }

    private static string TaskNodeId(Data.Entities.KernelTask task)
    {
        var separator = task.Id.IndexOf(':');
        return separator < 0 ? task.Id : task.Id[(separator + 1)..];
    }
}

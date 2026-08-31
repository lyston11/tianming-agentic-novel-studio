using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Goals;

namespace TM.Web.NovelAgentWeb.Services.Execution;

public sealed record GoalBudgetReservationRequest(
    string UserId,
    string ProjectId,
    string GoalId,
    string TaskId,
    string KernelName,
    string ModelConfigVersionId,
    string Provider,
    string Model,
    decimal WorstCaseCost,
    int Attempt,
    string OperationKey = "default");

public sealed record GoalBudgetReservationResult(
    bool Reserved,
    string? ExecutionId,
    decimal ReservedCost,
    decimal RemainingBudget);

public sealed record ModelExecutionResultRecord(
    string UserId,
    string ExecutionId,
    string ResultJson,
    decimal ActualCost,
    int InputTokens,
    int OutputTokens,
    string? ProviderRequestId,
    string ResultContentHash,
    string? Provider = null,
    string? Model = null,
    string? ProviderLeaseOwner = null);

public sealed record ModelExecutionResultReceipt(
    string ExecutionId,
    string ResultJson,
    bool RequiresSettlement);

public interface IGoalBudgetService
{
    Task<GoalBudgetReservationResult> ReserveAsync(
        GoalBudgetReservationRequest request,
        CancellationToken cancellationToken = default);

    Task<ModelExecutionResultReceipt?> FindReusableResultAsync(
        string userId,
        string taskId,
        string kernelName,
        string modelConfigVersionId,
        CancellationToken cancellationToken = default);

    Task<ModelExecutionResultReceipt?> FindReusableResultAsync(
        string userId,
        string taskId,
        string kernelName,
        string modelConfigVersionId,
        string operationKey,
        CancellationToken cancellationToken = default);

    Task MarkProviderCallStartedAsync(
        string userId,
        string executionId,
        string providerLeaseOwner,
        TimeSpan providerTimeout,
        CancellationToken cancellationToken = default);

    Task RecordResultAsync(
        ModelExecutionResultRecord result,
        CancellationToken cancellationToken = default);

    Task SettleAsync(
        string userId,
        string executionId,
        CancellationToken cancellationToken = default);

    Task FailKnownAsync(
        string userId,
        string executionId,
        string providerLeaseOwner,
        string errorMessage,
        CancellationToken cancellationToken = default);

    Task ChargeOutcomeUnknownAsync(
        string userId,
        string executionId,
        CancellationToken cancellationToken = default);
}

public sealed class GoalBudgetService : IGoalBudgetService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NovelAgentDbContext _db;

    public GoalBudgetService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<GoalBudgetReservationResult> ReserveAsync(
        GoalBudgetReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.WorstCaseCost < 0)
            throw new ArgumentOutOfRangeException(nameof(request.WorstCaseCost));
        if (request.Attempt is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(request.Attempt));
        if (_db.Database.GetDbConnection() is not NpgsqlConnection)
            throw new InvalidOperationException("Goal 预算原子预留只支持 PostgreSQL。");

        var idempotencyKey = BuildIdempotencyKey(request);
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var existing = await _db.ModelExecutions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == request.UserId && item.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing != null)
        {
            if (existing.Status is not ("reserved" or "result_received" or "completed"))
                throw new InvalidOperationException($"模型执行 {existing.Id} 已处于 {existing.Status}，不能在同一 attempt 重复预留。");
            await transaction.CommitAsync(cancellationToken);
            return await ExistingReservationAsync(existing, cancellationToken);
        }

        var updated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE creative_goals AS goal
            SET reserved_cost = reserved_cost + {request.WorstCaseCost},
                aggregate_version = aggregate_version + 1,
                status = CASE WHEN status = 'committed' THEN 'running' ELSE status END
            WHERE goal.id = {request.GoalId}
              AND goal.user_id = {request.UserId}
              AND goal.project_id = {request.ProjectId}
              AND goal.status IN ('committed', 'running', 'resumed')
              AND goal.actual_cost + goal.reserved_cost + {request.WorstCaseCost} <=
                  COALESCE(
                      (
                          SELECT (revision.constraint_changes_json::jsonb ->> 'totalCostLimit')::numeric
                          FROM goal_revisions AS revision
                          WHERE revision.user_id = goal.user_id
                            AND revision.goal_id = goal.id
                            AND revision.constraint_changes_json::jsonb ? 'totalCostLimit'
                            AND NULLIF(revision.constraint_changes_json::jsonb ->> 'totalCostLimit', '') IS NOT NULL
                          ORDER BY revision.revision_number DESC
                          LIMIT 1
                      ),
                      goal.total_cost_limit)
            """, cancellationToken);
        if (updated == 0)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE creative_goals AS goal
                SET status = 'budget_exceeded',
                    aggregate_version = aggregate_version + 1
                WHERE goal.id = {request.GoalId}
                  AND goal.user_id = {request.UserId}
                  AND goal.project_id = {request.ProjectId}
                  AND goal.status IN ('committed', 'running', 'resumed')
                  AND goal.actual_cost + goal.reserved_cost + {request.WorstCaseCost} >
                      COALESCE(
                          (
                              SELECT (revision.constraint_changes_json::jsonb ->> 'totalCostLimit')::numeric
                              FROM goal_revisions AS revision
                              WHERE revision.user_id = goal.user_id
                                AND revision.goal_id = goal.id
                                AND revision.constraint_changes_json::jsonb ? 'totalCostLimit'
                                AND NULLIF(revision.constraint_changes_json::jsonb ->> 'totalCostLimit', '') IS NOT NULL
                              ORDER BY revision.revision_number DESC
                              LIMIT 1
                          ),
                          goal.total_cost_limit)
                """, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            var deniedGoal = await _db.CreativeGoals.AsNoTracking().SingleOrDefaultAsync(goal =>
                goal.Id == request.GoalId && goal.UserId == request.UserId && goal.ProjectId == request.ProjectId,
                cancellationToken) ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
            var effectiveLimit = await GetEffectiveCostLimitAsync(request.UserId, request.GoalId, cancellationToken);
            return new GoalBudgetReservationResult(
                false,
                null,
                0,
                Math.Max(0, effectiveLimit - deniedGoal.ActualCost - deniedGoal.ReservedCost));
        }

        var execution = new ModelExecution
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            GoalId = request.GoalId,
            TaskId = request.TaskId,
            KernelName = request.KernelName,
            ModelConfigVersionId = request.ModelConfigVersionId,
            Provider = request.Provider,
            Model = request.Model,
            IdempotencyKey = idempotencyKey,
            OperationKey = request.OperationKey,
            Status = "reserved",
            ReservedCost = request.WorstCaseCost,
            Attempt = request.Attempt
        };
        _db.ModelExecutions.Add(execution);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            var winner = await _db.ModelExecutions.AsNoTracking().SingleAsync(item =>
                item.UserId == request.UserId && item.IdempotencyKey == idempotencyKey,
                cancellationToken);
            return await ExistingReservationAsync(winner, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        var goal = await _db.CreativeGoals.AsNoTracking().SingleAsync(item =>
            item.Id == request.GoalId && item.UserId == request.UserId,
            cancellationToken);
        var limit = await GetEffectiveCostLimitAsync(request.UserId, request.GoalId, cancellationToken);
        return new GoalBudgetReservationResult(
            true,
            execution.Id,
            request.WorstCaseCost,
            Math.Max(0, limit - goal.ActualCost - goal.ReservedCost));
    }

    public async Task<ModelExecutionResultReceipt?> FindReusableResultAsync(
        string userId,
        string taskId,
        string kernelName,
        string modelConfigVersionId,
        CancellationToken cancellationToken = default) =>
        await FindReusableResultAsync(
            userId,
            taskId,
            kernelName,
            modelConfigVersionId,
            "default",
            cancellationToken);

    public async Task<ModelExecutionResultReceipt?> FindReusableResultAsync(
        string userId,
        string taskId,
        string kernelName,
        string modelConfigVersionId,
        string operationKey,
        CancellationToken cancellationToken = default)
    {
        var execution = await _db.ModelExecutions.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.TaskId == taskId &&
                item.KernelName == kernelName &&
                item.ModelConfigVersionId == modelConfigVersionId &&
                item.OperationKey == operationKey &&
                item.ResultJson != null &&
                (item.Status == "result_received" || item.Status == "completed"))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return execution == null
            ? null
            : new ModelExecutionResultReceipt(
                execution.Id,
                execution.ResultJson!,
                execution.Status == "result_received");
    }

    public async Task MarkProviderCallStartedAsync(
        string userId,
        string executionId,
        string providerLeaseOwner,
        TimeSpan providerTimeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerLeaseOwner))
            throw new ArgumentException("Provider lease owner 不能为空。", nameof(providerLeaseOwner));
        if (providerTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(providerTimeout));

        var leaseDuration = providerTimeout + TimeSpan.FromSeconds(30);
        var updated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE model_executions
            SET status = 'running',
                started_at = COALESCE(started_at, clock_timestamp()),
                provider_lease_owner = {providerLeaseOwner},
                lease_expires_at = clock_timestamp() + {leaseDuration}
            WHERE id = {executionId}
              AND user_id = {userId}
              AND status = 'reserved'
            """, cancellationToken);
        if (updated == 1)
            return;

        _db.ChangeTracker.Clear();
        var existing = await _db.ModelExecutions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == executionId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("模型执行不存在或不属于当前用户。");
        if (existing.Status == "running" &&
            existing.ProviderLeaseOwner == providerLeaseOwner &&
            existing.LeaseExpiresAt > DateTime.UtcNow)
            return;
        throw new InvalidOperationException($"模型执行 {executionId} 处于 {existing.Status}，不能开始 provider 调用。");
    }

    public async Task RecordResultAsync(
        ModelExecutionResultRecord result,
        CancellationToken cancellationToken = default)
    {
        if (result.ActualCost < 0)
            throw new ArgumentOutOfRangeException(nameof(result.ActualCost));
        if (result.InputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(result.InputTokens));
        if (result.OutputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(result.OutputTokens));
        using (var document = JsonDocument.Parse(result.ResultJson))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("模型结果必须是 JSON object。", nameof(result.ResultJson));
        }

        var updated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE model_executions
            SET status = 'result_received',
                actual_cost = {result.ActualCost},
                input_tokens = {result.InputTokens},
                output_tokens = {result.OutputTokens},
                provider = COALESCE({result.Provider}, provider),
                model = COALESCE({result.Model}, model),
                provider_request_id = {result.ProviderRequestId},
                result_content_hash = {result.ResultContentHash},
                result_json = {result.ResultJson}::jsonb,
                lease_expires_at = NULL
            WHERE id = {result.ExecutionId}
              AND user_id = {result.UserId}
              AND status = 'running'
              AND provider_lease_owner = {result.ProviderLeaseOwner}
              AND reserved_cost >= {result.ActualCost}
            """, cancellationToken);
        if (updated == 1)
            return;

        _db.ChangeTracker.Clear();
        var existing = await _db.ModelExecutions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == result.ExecutionId && item.UserId == result.UserId,
            cancellationToken) ?? throw new KeyNotFoundException("模型执行不存在或不属于当前用户。");
        if (existing.Status is "result_received" or "completed" &&
            existing.ActualCost == result.ActualCost &&
            existing.InputTokens == result.InputTokens &&
            existing.OutputTokens == result.OutputTokens &&
            (result.Provider == null || existing.Provider == result.Provider) &&
            (result.Model == null || existing.Model == result.Model) &&
            existing.ProviderLeaseOwner == result.ProviderLeaseOwner &&
            existing.ProviderRequestId == result.ProviderRequestId &&
            existing.ResultContentHash == result.ResultContentHash &&
            existing.ResultJson == result.ResultJson)
        {
            return;
        }
        if (result.ActualCost > existing.ReservedCost)
            throw new InvalidOperationException("实际成本不能超过调用前预留的最坏成本。");
        throw new InvalidOperationException($"模型执行 {result.ExecutionId} 处于 {existing.Status}，不能记录不同结果。");
    }

    public async Task SettleAsync(
        string userId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        if (_db.Database.GetDbConnection() is not NpgsqlConnection)
            throw new InvalidOperationException("模型执行幂等结算只支持 PostgreSQL。");

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var execution = await _db.ModelExecutions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == executionId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("模型执行不存在或不属于当前用户。");
        if (execution.Status == "completed")
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        if (execution.Status != "result_received")
            throw new InvalidOperationException($"模型执行 {executionId} 尚未持久化可结算结果。");

        var claimed = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE model_executions
            SET status = 'settling'
            WHERE id = {executionId}
              AND user_id = {userId}
              AND status = 'result_received'
            """, cancellationToken);
        if (claimed == 0)
        {
            _db.ChangeTracker.Clear();
            var currentStatus = await _db.ModelExecutions.AsNoTracking()
                .Where(item => item.Id == executionId && item.UserId == userId)
                .Select(item => item.Status)
                .SingleAsync(cancellationToken);
            if (currentStatus != "completed")
                throw new InvalidOperationException($"模型执行 {executionId} 的结算状态已变为 {currentStatus}。");
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var goalUpdated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE creative_goals AS goal
            SET reserved_cost = GREATEST(0, goal.reserved_cost - {execution.ReservedCost}),
                actual_cost = goal.actual_cost + {execution.ActualCost},
                aggregate_version = goal.aggregate_version + 1,
                status = CASE
                    WHEN goal.actual_cost + {execution.ActualCost} >=
                        COALESCE(
                            (
                                SELECT (revision.constraint_changes_json::jsonb ->> 'totalCostLimit')::numeric
                                FROM goal_revisions AS revision
                                WHERE revision.user_id = goal.user_id
                                  AND revision.goal_id = goal.id
                                  AND revision.constraint_changes_json::jsonb ? 'totalCostLimit'
                                  AND NULLIF(revision.constraint_changes_json::jsonb ->> 'totalCostLimit', '') IS NOT NULL
                                ORDER BY revision.revision_number DESC
                                LIMIT 1
                            ),
                            goal.total_cost_limit)
                    THEN 'budget_exceeded'
                    ELSE goal.status
                END
            WHERE goal.id = {execution.GoalId}
              AND goal.user_id = {userId}
              AND goal.reserved_cost >= {execution.ReservedCost}
            """, cancellationToken);
        if (goalUpdated != 1)
            throw new InvalidOperationException("Goal 预算结算失败：预留金额或用户作用域不匹配。");

        var completed = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE model_executions
            SET status = 'completed',
                completed_at = clock_timestamp()
            WHERE id = {executionId}
              AND user_id = {userId}
              AND status = 'settling'
            """, cancellationToken);
        if (completed != 1)
            throw new InvalidOperationException("模型执行结算状态转换失败。");
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FailKnownAsync(
        string userId,
        string executionId,
        string providerLeaseOwner,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerLeaseOwner))
            throw new ArgumentException("Provider lease owner 不能为空。", nameof(providerLeaseOwner));
        if (_db.Database.GetDbConnection() is not NpgsqlConnection)
            throw new InvalidOperationException("模型执行失败结算只支持 PostgreSQL。");

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var execution = await _db.ModelExecutions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == executionId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("模型执行不存在或不属于当前用户。");
        if (execution.Status == "failed")
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        if (execution.Status != "running" || execution.ProviderLeaseOwner != providerLeaseOwner)
            throw new InvalidOperationException($"模型执行 {executionId} 处于 {execution.Status}，不能记录已知失败。");

        var goalUpdated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE creative_goals
            SET reserved_cost = GREATEST(0, reserved_cost - {execution.ReservedCost}),
                aggregate_version = aggregate_version + 1
            WHERE id = {execution.GoalId}
              AND user_id = {userId}
              AND reserved_cost >= {execution.ReservedCost}
            """, cancellationToken);
        if (goalUpdated != 1)
            throw new InvalidOperationException("Goal 已知失败结算失败：预留金额或用户作用域不匹配。");

        var executionUpdated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE model_executions
            SET status = 'failed',
                error_message = {errorMessage},
                lease_expires_at = NULL,
                completed_at = clock_timestamp()
            WHERE id = {executionId}
              AND user_id = {userId}
              AND status = 'running'
              AND provider_lease_owner = {providerLeaseOwner}
            """, cancellationToken);
        if (executionUpdated != 1)
            throw new InvalidOperationException("模型执行已知失败状态转换失败。");
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ChargeOutcomeUnknownAsync(
        string userId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var execution = await _db.ModelExecutions.SingleOrDefaultAsync(item =>
            item.Id == executionId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("模型执行不存在或不属于当前用户。");
        var effectiveLimit = await GetEffectiveCostLimitAsync(
            userId,
            execution.GoalId,
            cancellationToken);
        if (execution.ActualCost == 0 && execution.ReservedCost > 0)
        {
            var goal = await _db.CreativeGoals.SingleAsync(item =>
                item.Id == execution.GoalId && item.UserId == userId,
                cancellationToken);
            goal.ReservedCost -= execution.ReservedCost;
            goal.ActualCost += execution.ReservedCost;
            goal.AggregateVersion++;
            if (goal.ActualCost >= effectiveLimit)
                goal.Status = "budget_exceeded";
            execution.ActualCost = execution.ReservedCost;
        }
        execution.Status = "outcome_unknown";
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);
    }

    private async Task<GoalBudgetReservationResult> ExistingReservationAsync(
        ModelExecution execution,
        CancellationToken cancellationToken)
    {
        var goal = await _db.CreativeGoals.AsNoTracking().SingleAsync(item =>
            item.Id == execution.GoalId && item.UserId == execution.UserId,
            cancellationToken);
        var limit = await GetEffectiveCostLimitAsync(execution.UserId, execution.GoalId, cancellationToken);
        return new GoalBudgetReservationResult(
            true,
            execution.Id,
            execution.ReservedCost,
            Math.Max(0, limit - goal.ActualCost - goal.ReservedCost));
    }

    private async Task<decimal> GetEffectiveCostLimitAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken)
    {
        var committedLimit = await _db.CreativeGoals.AsNoTracking()
            .Where(item => item.Id == goalId && item.UserId == userId)
            .Select(item => item.TotalCostLimit)
            .SingleAsync(cancellationToken);
        var revisions = await _db.GoalRevisions.AsNoTracking()
            .Where(item => item.GoalId == goalId && item.UserId == userId)
            .OrderByDescending(item => item.RevisionNumber)
            .Select(item => item.ConstraintChangesJson)
            .ToListAsync(cancellationToken);
        foreach (var revisionJson in revisions)
        {
            var changes = JsonSerializer.Deserialize<GoalConstraintChanges>(revisionJson, JsonOptions);
            if (changes?.TotalCostLimit is { } revisedLimit)
                return revisedLimit;
        }
        return committedLimit;
    }

    private static string BuildIdempotencyKey(GoalBudgetReservationRequest request)
    {
        var canonical = string.Join('\u001f',
            request.UserId,
            request.ProjectId,
            request.GoalId,
            request.TaskId,
            request.KernelName,
            request.ModelConfigVersionId,
            request.OperationKey,
            request.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using Tianming.NovelAgent.Application.Ports;

namespace TM.Web.NovelAgentWeb.Services.Execution;

public enum ProviderExecutionOutcomeStatus
{
    Found,
    NotFound,
    Unsupported
}

public sealed record ProviderExecutionOutcome(
    ProviderExecutionOutcomeStatus Status,
    string? ContentJson,
    string? ContentHash);

public interface IModelExecutionOutcomeResolver
{
    Task<ProviderExecutionOutcome> QueryAsync(
        ModelExecution execution,
        CancellationToken cancellationToken = default);
}

public sealed class UnsupportedModelExecutionOutcomeResolver : IModelExecutionOutcomeResolver
{
    public Task<ProviderExecutionOutcome> QueryAsync(
        ModelExecution execution,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProviderExecutionOutcome(
            ProviderExecutionOutcomeStatus.Unsupported,
            null,
            null));
}

public enum ModelExecutionRecoveryAction
{
    StoredUnadoptedResult,
    RetryScheduled,
    GoalTerminated
}

public sealed record ModelExecutionRecoveryResult(
    ModelExecutionRecoveryAction Action,
    string ExecutionId,
    string? ArtifactId = null);

public interface IModelExecutionRecoveryService
{
    Task<ModelExecutionRecoveryResult> RecoverAsync(
        string userId,
        string executionId,
        CancellationToken cancellationToken = default);

    Task<string> RecordLateResultAsync(
        string userId,
        string executionId,
        string contentJson,
        string contentHash,
        CancellationToken cancellationToken = default);
}

public sealed class ModelExecutionRecoveryService : IModelExecutionRecoveryService
{
    private readonly NovelAgentDbContext _db;
    private readonly IModelExecutionOutcomeResolver _provider;
    private readonly IGoalBudgetService _budget;
    private readonly ILegacyControlPlaneCommands _controlPlane;

    public ModelExecutionRecoveryService(
        NovelAgentDbContext db,
        IModelExecutionOutcomeResolver provider)
        : this(db, provider, new GoalBudgetService(db), LegacyControlPlaneCommands.Unconfigured)
    {
    }

    public ModelExecutionRecoveryService(
        NovelAgentDbContext db,
        IModelExecutionOutcomeResolver provider,
        IGoalBudgetService budget)
        : this(db, provider, budget, LegacyControlPlaneCommands.Unconfigured)
    {
    }

    public ModelExecutionRecoveryService(
        NovelAgentDbContext db,
        IModelExecutionOutcomeResolver provider,
        IGoalBudgetService budget,
        ILegacyControlPlaneCommands controlPlane)
    {
        _db = db;
        _provider = provider;
        _budget = budget;
        _controlPlane = controlPlane;
    }

    public async Task<ModelExecutionRecoveryResult> RecoverAsync(
        string userId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var execution = await _db.ModelExecutions.SingleOrDefaultAsync(item =>
            item.Id == executionId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("模型执行不存在或不属于当前用户。");
        if (execution.Status is not ("reserved" or "running" or "outcome_unknown"))
            throw new InvalidOperationException("模型执行当前状态不允许崩溃恢复。");

        await _budget.ChargeOutcomeUnknownAsync(userId, executionId, cancellationToken);
        _db.ChangeTracker.Clear();
        execution = await _db.ModelExecutions.SingleAsync(item =>
            item.Id == executionId && item.UserId == userId,
            cancellationToken);
        var outcome = await _provider.QueryAsync(execution, cancellationToken);
        if (outcome.Status == ProviderExecutionOutcomeStatus.Found)
        {
            if (string.IsNullOrWhiteSpace(outcome.ContentJson) || string.IsNullOrWhiteSpace(outcome.ContentHash))
                throw new InvalidOperationException("Provider 返回 Found 时必须包含结果正文和内容哈希。");
            var artifactId = await AddUnadoptedArtifactAsync(
                execution,
                outcome.ContentJson,
                outcome.ContentHash,
                "RecoveredModelResult",
                cancellationToken);
            execution.Status = "recovered_unadopted";
            execution.ResultContentHash = outcome.ContentHash;
            execution.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return new ModelExecutionRecoveryResult(
                ModelExecutionRecoveryAction.StoredUnadoptedResult,
                execution.Id,
                artifactId);
        }

        var task = await _db.KernelTasks.SingleAsync(item =>
            item.Id == execution.TaskId && item.UserId == userId,
            cancellationToken);
        if (execution.Attempt < 2)
        {
            execution.Status = "retry_scheduled";
            execution.ErrorMessage = outcome.Status.ToString();
            execution.CompletedAt = DateTime.UtcNow;
            task.Status = "ready";
            task.MaxAttempts = Math.Max(task.MaxAttempts, 2);
            task.LeaseOwner = null;
            task.LeaseExpiresAt = null;
            task.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return new ModelExecutionRecoveryResult(
                ModelExecutionRecoveryAction.RetryScheduled,
                execution.Id);
        }

        execution.Status = "failed";
        execution.ErrorMessage = "模型调用结果无法确认，自动重试一次后仍失败。";
        execution.CompletedAt = DateTime.UtcNow;
        task.Status = "failed";
        task.LeaseOwner = null;
        task.LeaseExpiresAt = null;
        task.UpdatedAt = DateTime.UtcNow;
        var goal = await _db.CreativeGoals.SingleAsync(item =>
            item.Id == execution.GoalId && item.UserId == userId,
            cancellationToken);
        goal.Status = "failed";
        goal.AggregateVersion++;
        await _db.SaveChangesAsync(cancellationToken);
        return new ModelExecutionRecoveryResult(
            ModelExecutionRecoveryAction.GoalTerminated,
            execution.Id);
    }

    public async Task<string> RecordLateResultAsync(
        string userId,
        string executionId,
        string contentJson,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var execution = await _db.ModelExecutions.SingleOrDefaultAsync(item =>
            item.Id == executionId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("模型执行不存在或不属于当前用户。");
        var artifactId = await AddUnadoptedArtifactAsync(
            execution,
            contentJson,
            contentHash,
            "LateModelResult",
            cancellationToken);
        execution.Status = "late_result_unadopted";
        execution.ResultContentHash = contentHash;
        execution.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return artifactId;
    }

    private async Task<string> AddUnadoptedArtifactAsync(
        ModelExecution execution,
        string contentJson,
        string contentHash,
        string artifactType,
        CancellationToken cancellationToken)
    {
        using var _ = JsonDocument.Parse(contentJson);
        var existing = await _db.KernelArtifacts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == execution.UserId && item.ModelExecutionId == execution.Id &&
            item.ArtifactType == artifactType && item.ContentHash == contentHash,
            cancellationToken);
        if (existing != null)
            return existing.Id;
        var task = await _db.KernelTasks.AsNoTracking().SingleAsync(item =>
            item.Id == execution.TaskId && item.UserId == execution.UserId,
            cancellationToken);
        var artifactId = Guid.NewGuid().ToString("N");
        await _controlPlane.CreateArtifactAsync(new LegacyArtifactCommand(
            execution.UserId,
            execution.ProjectId,
            execution.GoalId,
            execution.TaskId,
            artifactId,
            task.BranchId,
            artifactType,
            SchemaVersion: 1,
            contentJson,
            contentHash,
            "unadopted",
            "agent",
            IsProtected: false,
            execution.Id,
            CausationId: null,
            DateTime.UtcNow),
            cancellationToken);
        return artifactId;
    }
}

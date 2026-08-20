using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Production;
using Tianming.NovelAgent.Application.Ports;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed record AdvanceAfterAcceptanceResult(
    BranchMergeRecord Merge,
    BookProductionAdvanceResult? Advance);

public interface IBookProductionTransitionService
{
    Task<AdvanceAfterAcceptanceResult> AdvanceAfterAcceptanceAsync(
        string goalId,
        string branchId,
        string actor,
        CancellationToken cancellationToken = default);

    Task<TaskGraphDefinition> ContinueInteractiveAsync(
        string goalId,
        CancellationToken cancellationToken = default);

    Task<BookProduction> ChangeStrategyAsync(
        string goalId,
        string executionStrategy,
        CancellationToken cancellationToken = default);

    Task<string> RequestPauseAsync(
        string goalId,
        CancellationToken cancellationToken = default);

    Task ResumeAsync(
        string goalId,
        CancellationToken cancellationToken = default);

    Task CancelAsync(
        string goalId,
        GoalCancellationStrategy strategy,
        CancellationToken cancellationToken = default);

    Task BlockAsync(
        string goalId,
        string batchId,
        CancellationToken cancellationToken = default);

    Task ApplyTaskFailureAsync(
        KernelTaskClaim claim,
        KernelTaskFailureDisposition disposition,
        CancellationToken cancellationToken = default);
}

public sealed class BookProductionTransitionService : IBookProductionTransitionService
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICanonBranchService _branches;
    private readonly IPrefixMergeService _prefixMerge;
    private readonly IBookProductionService _productions;
    private readonly IGoalCompiler _compiler;
    private readonly IGoalProgressEventPublisher _progress;
    private readonly IGoalControlService _control;
    private readonly IContentDocumentService _documents;
    private readonly ILogger<BookProductionTransitionService> _logger;
    private readonly ILegacyControlPlaneCommands _controlPlane;
    private readonly IBookValidationService? _bookValidation;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public BookProductionTransitionService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        ICanonBranchService branches,
        IPrefixMergeService prefixMerge,
        IBookProductionService productions,
        IGoalCompiler compiler,
        IGoalProgressEventPublisher progress,
        IGoalControlService control,
        IContentDocumentService documents,
        ILogger<BookProductionTransitionService> logger,
        ILegacyControlPlaneCommands controlPlane,
        IBookValidationService? bookValidation = null)
    {
        _db = db;
        _currentUser = currentUser;
        _branches = branches;
        _prefixMerge = prefixMerge;
        _productions = productions;
        _compiler = compiler;
        _progress = progress;
        _control = control;
        _documents = documents;
        _logger = logger;
        _controlPlane = controlPlane;
        _bookValidation = bookValidation;
    }

    public BookProductionTransitionService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        ICanonBranchService branches,
        IPrefixMergeService prefixMerge,
        IBookProductionService productions,
        IGoalCompiler compiler,
        IGoalProgressEventPublisher progress,
        IGoalControlService control,
        IContentDocumentService documents,
        ILogger<BookProductionTransitionService> logger)
        : this(
            db,
            currentUser,
            branches,
            prefixMerge,
            productions,
            compiler,
            progress,
            control,
            documents,
            logger,
            LegacyControlPlaneCommands.Unconfigured)
    {
    }

    public async Task<AdvanceAfterAcceptanceResult> AdvanceAfterAcceptanceAsync(
        string goalId,
        string branchId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        actor = BookProductionWorkflow.RequireActor(actor);
        var userId = _currentUser.GetUserId();
        var branch = await _db.CanonBranches.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == branchId && item.UserId == userId && item.GoalId == goalId,
                cancellationToken) ?? throw new KeyNotFoundException("候选分支不存在或不属于当前用户。");
            var batch = await _productions.GetCurrentBatchAsync(goalId, cancellationToken);
            if (batch.CanonBranchId != branch.Id ||
                batch.StartChapterNumber != branch.StartChapterNumber ||
                batch.EndChapterNumber != branch.EndChapterNumber)
                throw new InvalidOperationException("候选分支不属于当前生产批次。");
            if (batch.AcceptanceActor != actor)
                throw new InvalidOperationException("当前批次的验收主体与执行策略不匹配。");
            if (batch.Status is not ("running" or "accepting"))
                throw new InvalidOperationException("当前批次不在可验收状态。");
            var acceptedBatch = await _controlPlane.BeginBatchAcceptanceAsync(
                new LegacyBeginBatchAcceptanceCommand(
                    userId,
                    goalId,
                    batch.Id,
                    branch.Id,
                    actor),
                cancellationToken);
            if (acceptedBatch is null)
                throw new InvalidOperationException("当前批次已被其他验收流程占用。");
            batch = ToEntity(acceptedBatch);

            if (actor == BookProductionWorkflow.AgentPolicyActor)
            {
                var candidates = await _db.CandidateChapters.AsNoTracking()
                    .Where(item => item.UserId == userId && item.GoalId == goalId && item.BranchId == branchId)
                    .OrderBy(item => item.ChapterNumber)
                    .ThenByDescending(item => item.Version)
                    .ToListAsync(cancellationToken);
                var latest = candidates.GroupBy(item => item.ChapterNumber).Select(group => group.First()).ToArray();
                if (latest.Length != batch.EndChapterNumber - batch.StartChapterNumber + 1)
                    throw new InvalidOperationException("自动验收时批次候选章节不完整。");
                foreach (var candidate in latest)
                    await _branches.AcceptAsync(candidate.Id, candidate.Version, "agent", cancellationToken);
            }

            var merge = await _prefixMerge.MergeAcceptedPrefixAsync(branchId, cancellationToken);
            BookProductionAdvanceResult? advance = null;
            if (merge.EndChapterNumber == branch.EndChapterNumber)
            {
                advance = await FinalizeMergedBatchAsync(userId, goalId, branchId, cancellationToken);
                if (advance.ShouldCompileNextBatch)
                    await _compiler.CompileAsync(goalId, cancellationToken);
            }

            if (actor == BookProductionWorkflow.AgentPolicyActor)
            {
                await _progress.PublishAsync(new GoalProgressEventRequest(
                    userId,
                    goalId,
                    AgentSseEventType.GoalCandidateAccepted,
                    $"第 {batch.StartChapterNumber}-{batch.EndChapterNumber} 章已由 Agent 策略通过验收门。",
                    BranchId: branchId,
                    Action: BookProductionWorkflow.AgentPolicyActor), cancellationToken);
            }
            await _progress.PublishAsync(new GoalProgressEventRequest(
                userId,
                goalId,
                AgentSseEventType.GoalPrefixMerged,
                $"第 {merge.StartChapterNumber}-{merge.EndChapterNumber} 章已合并到正史。",
                BranchId: branchId,
                ArtifactIds: [$"merge-record:{merge.Id}"],
                Action: advance?.Production.Status ?? "prefix_merged"), cancellationToken);

            try
            {
                await _documents.PublishCommittedProjectChangesAsync(userId, branch.ProjectId, cancellationToken);
            }
            catch (Exception publicationException)
            {
                _logger.LogWarning(
                    publicationException,
                    "Book production transition {GoalId}/{BranchId} committed, but content publication failed.",
                    goalId,
                    branchId);
            }
            return new AdvanceAfterAcceptanceResult(merge, advance);
    }

    public async Task<TaskGraphDefinition> ContinueInteractiveAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        var batch = ToEntity(await _controlPlane.ContinueInteractiveBatchAsync(
            new LegacyWorkflowCommand(_currentUser.GetUserId(), goalId),
            cancellationToken));
        if (!string.IsNullOrWhiteSpace(batch.TaskGraphVersionId))
        {
            var active = await _db.TaskGraphVersions.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == batch.TaskGraphVersionId && item.Status == "active",
                cancellationToken);
            if (active != null)
                return JsonSerializer.Deserialize<TaskGraphDefinition>(active.GraphJson)
                    ?? throw new InvalidOperationException("当前批次任务图内容无效。");
        }
        return await _compiler.CompileAsync(goalId, cancellationToken);
    }

    public async Task<BookProduction> ChangeStrategyAsync(
        string goalId,
        string executionStrategy,
        CancellationToken cancellationToken = default)
    {
        var strategy = BookExecutionStrategies.RequireValid(executionStrategy);
        var production = await _controlPlane.ChangeExecutionStrategyAsync(
            new LegacyExecutionStrategyCommand(_currentUser.GetUserId(), goalId, strategy),
            cancellationToken);
        return ToEntity(production);
    }

    public async Task<string> RequestPauseAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
        try
        {
            var status = await _control.RequestPauseAsync(userId, goalId, cancellationToken)
                .ConfigureAwait(false);
            await _progress.PublishAsync(new GoalProgressEventRequest(
                userId,
                goalId,
                AgentSseEventType.GoalStateChanged,
                status == "paused" ? "Goal 已暂停。" : "Goal 将在下一个安全点暂停。",
                Action: status), cancellationToken).ConfigureAwait(false);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
            return status;
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task ResumeAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
        try
        {
            await _control.ResumeAsync(userId, goalId, cancellationToken).ConfigureAwait(false);
            await _progress.PublishAsync(new GoalProgressEventRequest(
                userId,
                goalId,
                AgentSseEventType.GoalStateChanged,
                "Goal 已恢复执行。",
                Action: "resumed"), cancellationToken).ConfigureAwait(false);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task CancelAsync(
        string goalId,
        GoalCancellationStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
        try
        {
            await _control.CancelAsync(userId, goalId, strategy, cancellationToken).ConfigureAwait(false);
            await _progress.PublishAsync(new GoalProgressEventRequest(
                userId,
                goalId,
                AgentSseEventType.GoalStateChanged,
                "Goal 已取消。",
                Action: strategy.ToString()), cancellationToken).ConfigureAwait(false);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task BlockAsync(
        string goalId,
        string batchId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        await _controlPlane.BlockBatchAsync(
            new LegacyBlockBatchCommand(userId, goalId, batchId),
            cancellationToken);
    }

    public async Task ApplyTaskFailureAsync(
        KernelTaskClaim claim,
        KernelTaskFailureDisposition disposition,
        CancellationToken cancellationToken = default)
    {
        if (disposition == KernelTaskFailureDisposition.Retry)
            return;
        if (claim.TaskId.Contains(":manual-rework-", StringComparison.Ordinal))
            return;
        var goalStatus = disposition == KernelTaskFailureDisposition.AwaitingDecision
            ? "awaiting_decision"
            : "failed";
        var productionStatus = disposition == KernelTaskFailureDisposition.AwaitingDecision
            ? "blocked"
            : "failed";
        await _controlPlane.ApplyTaskFailureAsync(
            new LegacyTaskFailureCommand(
                claim.UserId,
                claim.GoalId,
                goalStatus,
                productionStatus),
            cancellationToken);
    }

    private async Task<BookProductionAdvanceResult> FinalizeMergedBatchAsync(
        string userId,
        string goalId,
        string branchId,
        CancellationToken cancellationToken)
    {
        var production = await _db.BookProductions.AsNoTracking().SingleAsync(item =>
            item.UserId == userId && item.GoalId == goalId, cancellationToken);
        LegacyBookValidationArtifact? validationArtifact = null;
        if (production.NextChapterNumber <= production.TargetEndChapterNumber &&
            (await _productions.GetCurrentBatchAsync(goalId, cancellationToken)).EndChapterNumber >= production.TargetEndChapterNumber)
        {
            if (_bookValidation is null)
                throw new InvalidOperationException("Book validation is required before completing the final production batch.");
            var validation = await _bookValidation.ValidateAsync(new BookValidationRequest(
                userId,
                production.ProjectId,
                production.TargetStartChapterNumber,
                production.TargetEndChapterNumber), cancellationToken);
            var json = JsonSerializer.Serialize(validation);
            validationArtifact = new LegacyBookValidationArtifact(
                json,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(),
                validation.OverallStatus == "blocked" ? "unadopted" : "adopted");
        }

        var result = await _controlPlane.FinalizeMergedBatchAsync(
            new LegacyFinalizeMergedBatchCommand(
                userId,
                goalId,
                branchId,
                validationArtifact),
            cancellationToken);
        return new BookProductionAdvanceResult(
            ToEntity(result.Production),
            ToEntity(result.CompletedBatch),
            result.NextBatch is null ? null : ToEntity(result.NextBatch),
            result.ShouldCompileNextBatch);
    }

    private static BookProduction ToEntity(LegacyProductionState production) => new()
    {
        Id = production.Id,
        UserId = production.UserId,
        ProjectId = production.ProjectId,
        GoalId = production.GoalId,
        ExecutionStrategy = production.ExecutionStrategy,
        Status = production.Status,
        TargetStartChapterNumber = production.TargetStartChapterNumber,
        TargetEndChapterNumber = production.TargetEndChapterNumber,
        NextChapterNumber = production.NextChapterNumber,
        BatchSize = production.BatchSize,
        CurrentBatchNumber = production.CurrentBatchNumber,
        CompletionCriteriaJson = production.CompletionCriteriaJson,
        PausePolicyJson = production.PausePolicyJson,
        AggregateVersion = production.AggregateVersion,
        CreatedAt = production.CreatedAt.UtcDateTime,
        UpdatedAt = production.UpdatedAt.UtcDateTime,
        CompletedAt = production.CompletedAt?.UtcDateTime
    };

    private static ProductionBatch ToEntity(LegacyBatchState batch) => new()
    {
        Id = batch.Id,
        UserId = batch.UserId,
        ProjectId = batch.ProjectId,
        GoalId = batch.GoalId,
        BookProductionId = batch.BookProductionId,
        BatchNumber = batch.BatchNumber,
        StartChapterNumber = batch.StartChapterNumber,
        EndChapterNumber = batch.EndChapterNumber,
        Status = batch.Status,
        TaskGraphVersionId = batch.TaskGraphVersionId,
        CanonBranchId = batch.CanonBranchId,
        AcceptanceActor = batch.AcceptanceActor,
        CreatedAt = batch.CreatedAt.UtcDateTime,
        UpdatedAt = batch.UpdatedAt.UtcDateTime,
        CompletedAt = batch.CompletedAt?.UtcDateTime
    };

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction != null)
            return null;
        return await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }
}

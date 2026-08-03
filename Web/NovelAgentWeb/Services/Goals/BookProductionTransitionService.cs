using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Content;

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
    private readonly IContentDocumentService _documents;
    private readonly ILogger<BookProductionTransitionService> _logger;

    public BookProductionTransitionService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        ICanonBranchService branches,
        IPrefixMergeService prefixMerge,
        IBookProductionService productions,
        IGoalCompiler compiler,
        IGoalProgressEventPublisher progress,
        IContentDocumentService documents,
        ILogger<BookProductionTransitionService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _branches = branches;
        _prefixMerge = prefixMerge;
        _productions = productions;
        _compiler = compiler;
        _progress = progress;
        _documents = documents;
        _logger = logger;
    }

    public async Task<AdvanceAfterAcceptanceResult> AdvanceAfterAcceptanceAsync(
        string goalId,
        string branchId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        actor = BookProductionWorkflow.RequireActor(actor);
        var userId = _currentUser.GetUserId();
        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction == null)
            transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await AcquireTransitionLockAsync(userId, goalId, cancellationToken);
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
            batch.Status = "accepting";
            batch.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

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
                advance = await _productions.FinalizeMergedBatchAsync(goalId, branchId, cancellationToken);
                if (advance.ShouldCompileNextBatch)
                    await _compiler.CompileAsync(goalId, cancellationToken);
            }

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
            try
            {
                await _documents.PublishCommittedProjectChangesAsync(userId, branch.ProjectId, cancellationToken);
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
            }
            catch (Exception publicationException)
            {
                _logger.LogWarning(
                    publicationException,
                    "Book production transition {GoalId}/{BranchId} committed, but post-commit publication failed.",
                    goalId,
                    branchId);
            }
            return new AdvanceAfterAcceptanceResult(merge, advance);
        }
        catch
        {
            if (transaction != null && transaction.GetDbTransaction().Connection != null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<TaskGraphDefinition> ContinueInteractiveAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        var batch = await _productions.ContinueInteractiveAsync(goalId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(batch.TaskGraphVersionId))
        {
            var active = await _db.TaskGraphVersions.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == batch.TaskGraphVersionId && item.Status == "active",
                cancellationToken);
            if (active != null)
                return System.Text.Json.JsonSerializer.Deserialize<TaskGraphDefinition>(active.GraphJson)
                    ?? throw new InvalidOperationException("当前批次任务图内容无效。");
        }
        return await _compiler.CompileAsync(goalId, cancellationToken);
    }

    public async Task BlockAsync(
        string goalId,
        string batchId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var production = await _db.BookProductions.SingleAsync(item => item.UserId == userId && item.GoalId == goalId, cancellationToken);
        var batch = await _db.ProductionBatches.SingleAsync(item =>
            item.UserId == userId &&
            item.Id == batchId &&
            item.GoalId == goalId &&
            item.BookProductionId == production.Id &&
            item.BatchNumber == production.CurrentBatchNumber &&
            item.Status == "accepting",
            cancellationToken);
        var goal = await _db.CreativeGoals.SingleAsync(item => item.UserId == userId && item.Id == goalId, cancellationToken);
        production.Status = "blocked";
        production.UpdatedAt = DateTime.UtcNow;
        production.AggregateVersion++;
        batch.Status = "blocked";
        batch.UpdatedAt = DateTime.UtcNow;
        goal.Status = "awaiting_decision";
        goal.AggregateVersion++;
        await _db.SaveChangesAsync(cancellationToken);
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

    private async Task AcquireTransitionLockAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational() || _db.Database.GetDbConnection() is not Npgsql.NpgsqlConnection)
            return;
        var lockKey = $"book-production-transition\u001f{userId}\u001f{goalId}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}

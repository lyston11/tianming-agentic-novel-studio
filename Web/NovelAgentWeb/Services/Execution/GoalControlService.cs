using System.Text.Json;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Content;

namespace TM.Web.NovelAgentWeb.Services.Execution;

public enum GoalCancellationStrategy
{
    PreserveCandidateBranch,
    MergeAcceptedPrefix,
    DiscardCandidateBranch
}

public enum GoalSafePointDisposition
{
    Continue,
    Paused,
    Canceled,
    BudgetExceeded
}

public sealed record GoalSafePointResult(
    GoalSafePointDisposition Disposition,
    IReadOnlyList<string> ArtifactIds);

public interface IGoalControlService
{
    Task<string> RequestPauseAsync(string userId, string goalId, CancellationToken cancellationToken = default);
    Task ResumeAsync(string userId, string goalId, CancellationToken cancellationToken = default);
    Task CancelAsync(string userId, string goalId, GoalCancellationStrategy strategy, CancellationToken cancellationToken = default);
    Task<GoalSafePointResult> ReachSafePointAsync(
        KernelTaskClaim claim,
        IReadOnlyList<KernelArtifactProposal> artifacts,
        CancellationToken cancellationToken = default);
}

public sealed class GoalControlService : IGoalControlService
{
    private readonly NovelAgentDbContext _db;
    private readonly IPrefixMergeService _prefixMerge;
    private readonly IDistributedCacheService _cache;
    private readonly IVectorStore _vectors;
    private readonly IContentDocumentService _documents;

    public GoalControlService(
        NovelAgentDbContext db,
        IPrefixMergeService prefixMerge,
        IDistributedCacheService cache,
        IVectorStore vectors,
        IContentDocumentService documents)
    {
        _db = db;
        _prefixMerge = prefixMerge;
        _cache = cache;
        _vectors = vectors;
        _documents = documents;
    }

    public async Task<string> RequestPauseAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken = default)
    {
        var goal = await RequiredGoalAsync(userId, goalId, cancellationToken);
        if (goal.Status is not ("committed" or "running" or "resumed" or "pause_requested"))
            throw new InvalidOperationException("当前 Goal 状态不能请求暂停。");
        var hasRunningTask = await _db.KernelTasks.AsNoTracking().AnyAsync(item =>
            item.UserId == userId && item.GoalId == goalId && item.Status == "running",
            cancellationToken);
        if (hasRunningTask)
        {
            goal.Status = "pause_requested";
        }
        else
        {
            var claimableTasks = await _db.KernelTasks.Where(item =>
                item.UserId == userId &&
                item.GoalId == goalId &&
                (item.Status == "queued" || item.Status == "ready"))
                .ToListAsync(cancellationToken);
            foreach (var task in claimableTasks)
            {
                task.Status = "paused";
                task.LeaseOwner = null;
                task.LeaseExpiresAt = null;
                task.UpdatedAt = DateTime.UtcNow;
            }
            goal.Status = "paused";
        }
        goal.AggregateVersion++;
        await _db.SaveChangesAsync(cancellationToken);
        return goal.Status;
    }

    public async Task ResumeAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken = default)
    {
        var goal = await RequiredGoalAsync(userId, goalId, cancellationToken);
        if (goal.Status != "paused")
            throw new InvalidOperationException("只有已到达安全点的暂停 Goal 可以恢复。");
        var snapshotExists = await _db.GoalContextSnapshots.AsNoTracking().AnyAsync(item =>
            item.UserId == userId && item.GoalId == goalId && item.ProjectId == goal.ProjectId,
            cancellationToken);
        if (!snapshotExists)
            throw new InvalidOperationException("Goal 快照缺失，恢复前必须由用户决定如何重建基线。");
        var tasks = await _db.KernelTasks.Where(item =>
            item.UserId == userId && item.GoalId == goalId && item.Status == "paused")
            .ToListAsync(cancellationToken);
        foreach (var task in tasks)
        {
            task.Status = "ready";
            task.UpdatedAt = DateTime.UtcNow;
        }
        goal.Status = "resumed";
        goal.AggregateVersion++;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelAsync(
        string userId,
        string goalId,
        GoalCancellationStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational())
            transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var mergedAcceptedPrefix = false;
        var transactionCommitted = false;
        try
        {
        var goal = await RequiredGoalForUpdateAsync(userId, goalId, cancellationToken);
        if (goal.Status is "canceled" or "completed")
            throw new InvalidOperationException("Goal 已经结束，不能重复取消。");
        var branch = await _db.CanonBranches.SingleOrDefaultAsync(item =>
            item.UserId == userId && item.GoalId == goalId,
            cancellationToken);

        if (branch != null)
        {
            switch (strategy)
            {
                case GoalCancellationStrategy.PreserveCandidateBranch:
                    branch.Status = "preserved";
                    break;
                case GoalCancellationStrategy.MergeAcceptedPrefix:
                    await _prefixMerge.MergeAcceptedPrefixAsync(branch.Id, cancellationToken);
                    mergedAcceptedPrefix = true;
                    if (branch.Status != "merged")
                        branch.Status = "preserved";
                    break;
                case GoalCancellationStrategy.DiscardCandidateBranch:
                    await DiscardBranchAsync(userId, goalId, branch, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(strategy));
            }
            branch.UpdatedAt = DateTime.UtcNow;
        }

        var pendingTasks = await _db.KernelTasks.Where(item =>
            item.UserId == userId && item.GoalId == goalId &&
            (item.Status == "queued" || item.Status == "ready" || item.Status == "blocked" ||
             item.Status == "paused" || item.Status == "running"))
            .ToListAsync(cancellationToken);
        foreach (var task in pendingTasks)
        {
            task.Status = "canceled";
            task.LeaseOwner = null;
            task.LeaseExpiresAt = null;
            task.UpdatedAt = DateTime.UtcNow;
        }
        goal.Status = "canceled";
        goal.AggregateVersion++;
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
            transactionCommitted = true;
            if (mergedAcceptedPrefix)
            {
                await _documents.PublishCommittedProjectChangesAsync(
                    userId,
                    goal.ProjectId,
                    cancellationToken);
            }
        }
        }
        catch
        {
            if (transaction != null && !transactionCommitted)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<GoalSafePointResult> ReachSafePointAsync(
        KernelTaskClaim claim,
        IReadOnlyList<KernelArtifactProposal> artifacts,
        CancellationToken cancellationToken = default)
    {
        var goal = await _db.CreativeGoals.SingleAsync(item =>
            item.Id == claim.GoalId && item.UserId == claim.UserId && item.ProjectId == claim.ProjectId,
            cancellationToken);
        var disposition = goal.Status switch
        {
            "pause_requested" => GoalSafePointDisposition.Paused,
            "canceled" => GoalSafePointDisposition.Canceled,
            "budget_exceeded" => GoalSafePointDisposition.BudgetExceeded,
            _ => GoalSafePointDisposition.Continue
        };
        if (disposition == GoalSafePointDisposition.Continue)
            return new GoalSafePointResult(disposition, []);

        var ids = new List<string>(artifacts.Count);
        foreach (var proposal in artifacts)
        {
            var artifact = new KernelArtifact
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = claim.UserId,
                ProjectId = claim.ProjectId,
                GoalId = claim.GoalId,
                TaskId = claim.TaskId,
                BranchId = claim.BranchId,
                ArtifactType = proposal.ArtifactType,
                SchemaVersion = proposal.SchemaVersion,
                ContentJson = proposal.ContentJson,
                ContentHash = proposal.ContentHash,
                Status = "unadopted",
                Authorship = proposal.Authorship,
                IsProtected = proposal.IsProtected
            };
            _db.KernelArtifacts.Add(artifact);
            ids.Add(artifact.Id);
        }
        var task = await _db.KernelTasks.SingleAsync(item =>
            item.Id == claim.TaskId && item.UserId == claim.UserId && item.LeaseOwner == claim.LeaseOwner,
            cancellationToken);
        task.Status = disposition switch
        {
            GoalSafePointDisposition.Paused => "paused",
            GoalSafePointDisposition.BudgetExceeded => "budget_exceeded",
            _ => "canceled"
        };
        task.OutputArtifactIdsJson = JsonSerializer.Serialize(ids);
        task.LeaseOwner = null;
        task.LeaseExpiresAt = null;
        task.UpdatedAt = DateTime.UtcNow;
        if (disposition == GoalSafePointDisposition.Paused)
        {
            goal.Status = "paused";
            goal.AggregateVersion++;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return new GoalSafePointResult(disposition, ids);
    }

    private async Task DiscardBranchAsync(
        string userId,
        string goalId,
        CanonBranch branch,
        CancellationToken cancellationToken)
    {
        await _vectors.DeleteVectorsByFilterAsync(userId, new Dictionary<string, object>
        {
            ["branch_id"] = branch.Id
        }, cancellationToken);
        await _cache.RemoveByPrefixAsync($"goal:{userId}:{goalId}", cancellationToken);
        var acceptances = await _db.CandidateAcceptances.Where(item =>
            item.UserId == userId && item.GoalId == goalId).ToListAsync(cancellationToken);
        var candidates = await _db.CandidateChapters.Where(item =>
            item.UserId == userId && item.GoalId == goalId).ToListAsync(cancellationToken);
        var artifacts = await _db.KernelArtifacts.Where(item =>
            item.UserId == userId && item.GoalId == goalId && item.BranchId == branch.Id)
            .ToListAsync(cancellationToken);
        _db.CandidateAcceptances.RemoveRange(acceptances);
        _db.CandidateChapters.RemoveRange(candidates);
        _db.KernelArtifacts.RemoveRange(artifacts);
        branch.Status = "discarded";
    }

    private async Task<CreativeGoal> RequiredGoalAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken) =>
        await _db.CreativeGoals.SingleOrDefaultAsync(item => item.Id == goalId && item.UserId == userId, cancellationToken)
        ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");

    private async Task<CreativeGoal> RequiredGoalForUpdateAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational() &&
            _db.Database.GetDbConnection() is Npgsql.NpgsqlConnection)
        {
            return await _db.CreativeGoals.FromSqlInterpolated($"""
                SELECT * FROM creative_goals
                WHERE id = {goalId} AND user_id = {userId}
                FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
        }

        return await RequiredGoalAsync(userId, goalId, cancellationToken);
    }
}

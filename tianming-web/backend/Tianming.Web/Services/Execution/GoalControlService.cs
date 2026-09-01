using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using Tianming.NovelAgent.Application.Ports;

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

/// <summary>
/// Compatibility adapter for goal pause/resume/cancel/safe-point control.
/// Control-plane state mutations are owned by Application commands; this
/// service only orchestrates the canon-side side effects (prefix merge,
/// vector/cache cleanup, candidate deletion, change publication) around them.
/// </summary>
public sealed class GoalControlService : IGoalControlService
{
    private readonly NovelAgentDbContext _db;
    private readonly ILegacyControlPlaneCommands _controlPlane;
    private readonly IPrefixMergeService _prefixMerge;
    private readonly IDistributedCacheService _cache;
    private readonly IVectorStore _vectors;
    private readonly IContentDocumentService _documents;

    public GoalControlService(
        NovelAgentDbContext db,
        ILegacyControlPlaneCommands controlPlane,
        IPrefixMergeService prefixMerge,
        IDistributedCacheService cache,
        IVectorStore vectors,
        IContentDocumentService documents)
    {
        _db = db;
        _controlPlane = controlPlane;
        _prefixMerge = prefixMerge;
        _cache = cache;
        _vectors = vectors;
        _documents = documents;
    }

    public Task<string> RequestPauseAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken = default) =>
        _controlPlane.PauseGoalAsync(new LegacyGoalPauseCommand(userId, goalId), cancellationToken);

    public Task ResumeAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken = default) =>
        _controlPlane.ResumeGoalAsync(new LegacyGoalResumeCommand(userId, goalId), cancellationToken);

    public async Task CancelAsync(
        string userId,
        string goalId,
        GoalCancellationStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        var branchId = await _db.CanonBranches.AsNoTracking()
            .Where(item => item.UserId == userId && item.GoalId == goalId)
            .Select(item => (string?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var mergedAcceptedPrefix = false;
        string? finalBranchStatus = null;
        switch (strategy)
        {
            case GoalCancellationStrategy.PreserveCandidateBranch:
                if (branchId != null)
                    finalBranchStatus = "preserved";
                break;
            case GoalCancellationStrategy.MergeAcceptedPrefix:
                if (branchId != null)
                {
                    await _prefixMerge.MergeAcceptedPrefixAsync(branchId, cancellationToken);
                    mergedAcceptedPrefix = true;
                    // The prefix merge may have transitioned the branch to
                    // "merged"; preserve it only when it did not fully merge.
                    var mergedStatus = await _db.CanonBranches.AsNoTracking()
                        .Where(item => item.Id == branchId)
                        .Select(item => item.Status)
                        .SingleAsync(cancellationToken);
                    finalBranchStatus = mergedStatus == "merged" ? "merged" : "preserved";
                }
                break;
            case GoalCancellationStrategy.DiscardCandidateBranch:
                if (branchId != null)
                {
                    await DiscardBranchAsync(userId, goalId, branchId, cancellationToken);
                    finalBranchStatus = "discarded";
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(strategy));
        }

        await _controlPlane.CancelGoalAsync(new LegacyGoalCancelCommand(
            userId,
            goalId,
            finalBranchStatus,
            IdempotentRetry: false),
            cancellationToken);

        if (mergedAcceptedPrefix)
        {
            var projectId = await _db.CreativeGoals.AsNoTracking()
                .Where(item => item.UserId == userId && item.Id == goalId)
                .Select(item => item.ProjectId)
                .SingleAsync(cancellationToken);
            await _documents.PublishCommittedProjectChangesAsync(userId, projectId, cancellationToken);
        }
    }

    public async Task<GoalSafePointResult> ReachSafePointAsync(
        KernelTaskClaim claim,
        IReadOnlyList<KernelArtifactProposal> artifacts,
        CancellationToken cancellationToken = default)
    {
        var result = await _controlPlane.ReachSafePointAsync(new LegacySafePointCommand(
            claim.UserId,
            claim.ProjectId,
            claim.GoalId,
            claim.TaskId,
            claim.BranchId,
            claim.LeaseOwner,
            artifacts.Select(proposal => new LegacySafePointArtifact(
                proposal.ArtifactType,
                proposal.SchemaVersion,
                proposal.ContentJson,
                proposal.ContentHash,
                proposal.Authorship,
                proposal.IsProtected)).ToList()),
            cancellationToken);
        return new GoalSafePointResult(
            Enum.Parse<GoalSafePointDisposition>(result.Disposition),
            result.ArtifactIds);
    }

    /// <summary>
    /// Canon-side discard: vector/cache cleanup plus candidate-row deletion.
    /// Branch status and kernel artifact deletion are applied by the cancel
    /// command in the same Agent-control transaction as task cancellation.
    /// Both steps are idempotent so a failure between them can be retried.
    /// </summary>
    private async Task DiscardBranchAsync(
        string userId,
        string goalId,
        string branchId,
        CancellationToken cancellationToken)
    {
        await _vectors.DeleteVectorsByFilterAsync(userId, new Dictionary<string, object>
        {
            ["branch_id"] = branchId
        }, cancellationToken);
        await _cache.RemoveByPrefixAsync($"goal:{userId}:{goalId}", cancellationToken);
        var acceptances = await _db.CandidateAcceptances.Where(item =>
            item.UserId == userId && item.GoalId == goalId).ToListAsync(cancellationToken);
        var candidates = await _db.CandidateChapters.Where(item =>
            item.UserId == userId && item.GoalId == goalId).ToListAsync(cancellationToken);
        _db.CandidateAcceptances.RemoveRange(acceptances);
        _db.CandidateChapters.RemoveRange(candidates);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

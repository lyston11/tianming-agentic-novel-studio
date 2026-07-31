using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Execution;

namespace TM.Web.NovelAgentWeb.Services.Kernels;

public sealed class KernelTaskExecutionRouter : IKernelTaskExecutor
{
    private readonly NovelAgentDbContext _db;
    private readonly KernelRegistry _registry;
    private readonly IDomainReducer _reducer;
    private readonly IGoalControlService _goalControl;
    private readonly IKernelModelExecutionScopeAccessor? _modelExecutionScopes;

    public KernelTaskExecutionRouter(
        NovelAgentDbContext db,
        KernelRegistry registry,
        IDomainReducer reducer,
        IGoalControlService goalControl,
        IKernelModelExecutionScopeAccessor? modelExecutionScopes = null)
    {
        _db = db;
        _registry = registry;
        _reducer = reducer;
        _goalControl = goalControl;
        _modelExecutionScopes = modelExecutionScopes;
    }

    public async Task<KernelTaskExecutionResult> ExecuteAsync(
        KernelTaskClaim claim,
        CancellationToken cancellationToken = default)
    {
        var task = await _db.KernelTasks.AsNoTracking().SingleAsync(item =>
            item.Id == claim.TaskId &&
            item.UserId == claim.UserId &&
            item.TaskGraphVersionId == claim.TaskGraphVersionId,
            cancellationToken);
        var graphTasks = await _db.KernelTasks.AsNoTracking()
            .Where(item =>
                item.UserId == claim.UserId &&
                item.TaskGraphVersionId == claim.TaskGraphVersionId)
            .ToListAsync(cancellationToken);
        var byNodeId = graphTasks.ToDictionary(TaskNodeId, StringComparer.Ordinal);
        var dependencyNodeIds = JsonSerializer.Deserialize<string[]>(task.DependencyTaskIdsJson) ?? [];
        var artifactIds = new List<string>(
            JsonSerializer.Deserialize<string[]>(task.InputArtifactIdsJson) ?? []);
        foreach (var dependencyNodeId in dependencyNodeIds)
        {
            if (!byNodeId.TryGetValue(dependencyNodeId, out var dependency))
                throw new InvalidOperationException($"依赖任务不存在：{dependencyNodeId}");
            if (dependency.Status is not ("completed" or "reused"))
                throw new InvalidOperationException($"依赖任务尚未完成：{dependencyNodeId}");
            artifactIds.AddRange(JsonSerializer.Deserialize<string[]>(dependency.OutputArtifactIdsJson) ?? []);
        }

        artifactIds = artifactIds.Distinct(StringComparer.Ordinal).ToList();
        var inputArtifacts = artifactIds.Count == 0
            ? []
            : await _db.KernelArtifacts.AsNoTracking()
                .Where(artifact =>
                    artifact.UserId == claim.UserId &&
                    artifactIds.Contains(artifact.Id))
                .Select(artifact => new KernelInputArtifact(
                    artifact.Id,
                    artifact.ArtifactType,
                    artifact.SchemaVersion,
                    artifact.ContentJson,
                    artifact.ContentHash,
                    artifact.Authorship,
                    artifact.IsProtected))
                .ToListAsync(cancellationToken);
        if (inputArtifacts.Count != artifactIds.Count)
            throw new InvalidOperationException("依赖 Artifact 缺失或不属于当前用户。");
        var snapshot = await _db.GoalContextSnapshots.AsNoTracking().SingleAsync(item =>
            item.UserId == claim.UserId && item.GoalId == claim.GoalId,
            cancellationToken);
        var goalContract = await LoadGoalContractAsync(claim, cancellationToken);
        var context = new KernelExecutionContext(claim, snapshot, goalContract, inputArtifacts);
        KernelExecutionOutput output;
        if (claim.TaskType == "FreezeBaselines")
        {
            output = FreezeBaselines(context);
        }
        else
        {
            using var modelExecutionScope = _modelExecutionScopes?.Push(new KernelModelExecutionScope(
                claim.UserId,
                claim.ProjectId,
                claim.GoalId,
                claim.TaskId,
                claim.KernelName,
                claim.Attempt));
            output = await _registry.GetRequired(claim.KernelName)
                .ExecuteAsync(context, cancellationToken);
        }
        var safePoint = await _goalControl.ReachSafePointAsync(
            claim,
            output.Artifacts,
            cancellationToken);
        if (safePoint.Disposition != GoalSafePointDisposition.Continue)
        {
            return new KernelTaskExecutionResult(
                safePoint.ArtifactIds,
                safePoint.Disposition switch
                {
                    GoalSafePointDisposition.Paused => KernelTaskExecutionDisposition.Paused,
                    GoalSafePointDisposition.BudgetExceeded => KernelTaskExecutionDisposition.BudgetExceeded,
                    _ => KernelTaskExecutionDisposition.Canceled
                });
        }
        var adoption = await _reducer.ApplyAsync(
            claim,
            output.Artifacts,
            output.Events,
            cancellationToken);
        return new KernelTaskExecutionResult(adoption.ArtifactIds);
    }

    private static KernelExecutionOutput FreezeBaselines(KernelExecutionContext context)
    {
        var json = JsonSerializer.Serialize(new
        {
            context.GoalSnapshot.CanonVersion,
            context.GoalSnapshot.KnowledgeVersion,
            context.GoalSnapshot.QualityContractVersion,
            context.GoalSnapshot.StyleProfileVersion,
            context.GoalSnapshot.ModelConfigVersionsJson,
            context.GoalSnapshot.ProtocolVersionsJson,
            context.GoalSnapshot.ContentHashesJson,
            context.GoalContract,
            context.GoalSnapshot.CreatedAt
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new KernelExecutionOutput(
            [new KernelArtifactProposal("FrozenBaselines", 1, json, hash, "agent", false)],
            []);
    }

    private async Task<KernelGoalContract> LoadGoalContractAsync(
        KernelTaskClaim claim,
        CancellationToken cancellationToken)
    {
        var graph = await _db.TaskGraphVersions.AsNoTracking().SingleAsync(item =>
            item.Id == claim.TaskGraphVersionId &&
            item.UserId == claim.UserId &&
            item.GoalId == claim.GoalId,
            cancellationToken);
        var committedGoal = await _db.CreativeGoals.AsNoTracking().SingleAsync(item =>
            item.Id == claim.GoalId && item.UserId == claim.UserId,
            cancellationToken);
        int? targetRevisionNumber = null;
        if (graph.GoalRevisionId != null)
        {
            targetRevisionNumber = await _db.GoalRevisions.AsNoTracking()
                .Where(item =>
                    item.Id == graph.GoalRevisionId &&
                    item.GoalId == claim.GoalId &&
                    item.UserId == claim.UserId)
                .Select(item => (int?)item.RevisionNumber)
                .SingleAsync(cancellationToken)
                ?? throw new InvalidOperationException("任务图关联的 Goal Revision 缺少版本号。");
        }
        var revisions = targetRevisionNumber.HasValue
            ? await _db.GoalRevisions.AsNoTracking()
                .Where(item =>
                    item.GoalId == claim.GoalId &&
                    item.UserId == claim.UserId &&
                    item.RevisionNumber <= targetRevisionNumber.Value)
                .OrderBy(item => item.RevisionNumber)
                .ToListAsync(cancellationToken)
            : [];
        var goal = CreativeGoalRevisionProjector.Project(committedGoal, revisions);
        return new KernelGoalContract(
            goal.GoalType,
            goal.CollaborationMode,
            goal.HumanReadableObjective,
            goal.TargetChapterRangeJson,
            goal.SuccessCriteriaJson,
            goal.MustPreserveJson,
            goal.MustHappenJson,
            goal.MustNotChangeJson,
            goal.AcceptancePolicyJson,
            goal.ReworkPolicyJson);
    }

    private static string TaskNodeId(Data.Entities.KernelTask task)
    {
        var separator = task.Id.IndexOf(':');
        return separator < 0 ? task.Id : task.Id[(separator + 1)..];
    }
}

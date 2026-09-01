using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using Tianming.NovelAgent.Application.Ports;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class CreativeGoalService : ICreativeGoalService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IGoalBaselineProvider _baselines;
    private readonly ILegacyControlPlaneCommands _controlPlane;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public CreativeGoalService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        IGoalBaselineProvider baselines,
        ILegacyControlPlaneCommands controlPlane)
    {
        _db = db;
        _currentUser = currentUser;
        _baselines = baselines;
        _controlPlane = controlPlane;
    }

    public CreativeGoalService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        IGoalBaselineProvider baselines,
        IBookProductionService bookProductions)
        : this(db, currentUser, baselines, LegacyControlPlaneCommands.Unconfigured)
    {
    }

    public async Task<GoalSubmissionResult> SubmitAsync(
        CreateCreativeGoalCommand command,
        CommitmentAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        if (assessment.State != DialogueCommitmentState.Committed)
            return new GoalSubmissionResult(GoalSubmissionStatus.NotCommitted);

        if (assessment.RequiresConfirmation || assessment.Authorization == GoalAuthorizationKind.None)
            return new GoalSubmissionResult(GoalSubmissionStatus.NeedsConfirmation);

        if (assessment.ProposedContract == null ||
            !ContractsMatch(command.Contract, assessment.ProposedContract))
        {
            throw new InvalidOperationException("授权对应的目标合同与提交合同不一致。");
        }

        if (command.TotalCostLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.TotalCostLimit), "Goal 总金额上限必须大于零。");

        var userId = _currentUser.GetUserId();
        var ownsProject = await _db.NovelProjects
            .AsNoTracking()
            .AnyAsync(project => project.Id == command.ProjectId && project.UserId == userId, cancellationToken);
        if (!ownsProject)
            throw new KeyNotFoundException("项目不存在或不属于当前用户。");

        var sourceSessionId = RequireText(command.SourceSessionId, nameof(command.SourceSessionId));
        var idempotencyKey = RequireText(command.IdempotencyKey, nameof(command.IdempotencyKey));
        var existing = await _db.CreativeGoals
            .AsNoTracking()
            .FirstOrDefaultAsync(goal =>
                goal.UserId == userId &&
                goal.ProjectId == command.ProjectId &&
                goal.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing != null)
        {
            if (existing.Status is not ("completed" or "canceled" or "failed"))
                await _controlPlane.SubmitGoalAsync(ToSubmissionCommand(existing), cancellationToken);
            return new GoalSubmissionResult(GoalSubmissionStatus.Existing, existing.Id);
        }

        var ownsSourceSession = await _db.AgentSessions
            .AsNoTracking()
            .AnyAsync(session => session.Id == sourceSessionId && session.UserId == userId, cancellationToken);
        if (!ownsSourceSession)
            throw new KeyNotFoundException("来源会话不存在或不属于当前用户。");

        var frozen = await _baselines.CaptureAsync(userId, command.ProjectId, cancellationToken);
        var submission = await _controlPlane.SubmitGoalAsync(new LegacyGoalSubmissionCommand(
            userId,
            command.ProjectId,
            sourceSessionId,
            idempotencyKey,
            command.TotalCostLimit,
            RequireText(command.Contract.GoalType, nameof(command.Contract.GoalType)),
            RequireText(command.Contract.CollaborationMode, nameof(command.Contract.CollaborationMode)),
            RequireText(command.Contract.HumanReadableObjective, nameof(command.Contract.HumanReadableObjective)),
            command.Contract.TargetChapterRangeJson,
            JsonSerializer.Serialize(command.Contract.SuccessCriteria),
            JsonSerializer.Serialize(command.Contract.MustPreserve),
            JsonSerializer.Serialize(command.Contract.MustHappen),
            JsonSerializer.Serialize(command.Contract.MustNotChange),
            command.Contract.AcceptancePolicyJson,
            command.Contract.ReworkPolicyJson,
            BookExecutionStrategies.RequireValid(command.Contract.ExecutionStrategy),
            command.Contract.BookPlanJson,
            new LegacyFrozenBaselines(
                frozen.CanonVersion,
                frozen.KnowledgeVersion,
                frozen.QualityContractVersion,
                frozen.StyleProfileVersion,
                frozen.ModelConfigVersionsJson,
                frozen.ProtocolVersionsJson,
                frozen.ContentHashesJson)), cancellationToken);
        return new GoalSubmissionResult(
            submission.Existing ? GoalSubmissionStatus.Existing : GoalSubmissionStatus.Created,
            submission.GoalId);
    }

    public async Task<GoalRevision> ReviseAsync(
        ReviseCreativeGoalCommand command,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var goal = await _db.CreativeGoals
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == command.GoalId && item.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
        if (command.AffectedNodeIds.Count == 0 || command.AffectedNodeIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Goal Revision 必须声明受影响任务节点。", nameof(command.AffectedNodeIds));
        var changes = JsonSerializer.Deserialize<GoalConstraintChanges>(command.ConstraintChangesJson, JsonOptions)
            ?? throw new ArgumentException("Goal Revision 约束变化不能为空。", nameof(command.ConstraintChangesJson));
        if (changes.TotalCostLimit.HasValue && changes.TotalCostLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.ConstraintChangesJson), "修订后的 Goal 总金额上限必须大于零。");
        var revisionId = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;
        var revision = await _controlPlane.CreateGoalRevisionAsync(
            new LegacyGoalRevisionCommand(
                userId,
                goal.ProjectId,
                goal.Id,
                revisionId,
                0,
                RequireText(command.Reason, nameof(command.Reason)),
                JsonSerializer.Serialize(changes, JsonOptions),
                JsonSerializer.Serialize(command.ReusableArtifactIds),
                JsonSerializer.Serialize(command.InvalidatedArtifactIds),
                JsonSerializer.Serialize(command.AffectedNodeIds.Distinct(StringComparer.Ordinal)),
                createdAt),
            cancellationToken);
        return new GoalRevision
        {
            Id = revision.Id,
            UserId = userId,
            ProjectId = goal.ProjectId,
            GoalId = goal.Id,
            RevisionNumber = revision.RevisionNumber,
            Reason = revision.Reason,
            ConstraintChangesJson = revision.ConstraintChangesJson,
            ReusableArtifactIdsJson = revision.ReusableArtifactIdsJson,
            InvalidatedArtifactIdsJson = revision.InvalidatedArtifactIdsJson,
            AffectedNodeIdsJson = revision.AffectedNodeIdsJson,
            CreatedAt = revision.CreatedAt.UtcDateTime
        };
    }

    private static LegacyGoalSubmissionCommand ToSubmissionCommand(CreativeGoal existing) =>
        new(
            existing.UserId,
            existing.ProjectId,
            existing.SourceSessionId,
            existing.IdempotencyKey,
            existing.TotalCostLimit,
            existing.GoalType,
            existing.CollaborationMode,
            existing.HumanReadableObjective,
            existing.TargetChapterRangeJson,
            existing.SuccessCriteriaJson,
            existing.MustPreserveJson,
            existing.MustHappenJson,
            existing.MustNotChangeJson,
            existing.AcceptancePolicyJson,
            existing.ReworkPolicyJson,
            existing.ExecutionStrategy,
            existing.BookPlanJson,
            new LegacyFrozenBaselines(
                existing.CanonBaselineVersion,
                existing.KnowledgeSnapshotVersion,
                existing.QualityContractVersion,
                existing.StyleProfileVersion,
                existing.ModelConfigVersionsJson,
                existing.ProtocolVersionsJson,
                "{}"),
            existing.Id);
    private static bool ContractsMatch(CreativeGoalContract left, CreativeGoalContract right) =>
        JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("值不能为空。", parameterName)
            : value.Trim();
}

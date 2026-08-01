using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class CreativeGoalService : ICreativeGoalService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IGoalBaselineProvider _baselines;
    private readonly IBookProductionService _bookProductions;

    public CreativeGoalService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        IGoalBaselineProvider baselines,
        IBookProductionService bookProductions)
    {
        _db = db;
        _currentUser = currentUser;
        _baselines = baselines;
        _bookProductions = bookProductions;
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
                await _bookProductions.InitializeAsync(existing, cancellationToken);
            return new GoalSubmissionResult(GoalSubmissionStatus.Existing, existing.Id);
        }

        var ownsSourceSession = await _db.AgentSessions
            .AsNoTracking()
            .AnyAsync(session => session.Id == sourceSessionId && session.UserId == userId, cancellationToken);
        if (!ownsSourceSession)
            throw new KeyNotFoundException("来源会话不存在或不属于当前用户。");

        var frozen = await _baselines.CaptureAsync(userId, command.ProjectId, cancellationToken);
        var now = DateTime.UtcNow;
        var goal = new CreativeGoal
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = command.ProjectId,
            SourceSessionId = sourceSessionId,
            GoalType = RequireText(command.Contract.GoalType, nameof(command.Contract.GoalType)),
            CollaborationMode = RequireText(command.Contract.CollaborationMode, nameof(command.Contract.CollaborationMode)),
            HumanReadableObjective = RequireText(command.Contract.HumanReadableObjective, nameof(command.Contract.HumanReadableObjective)),
            TargetChapterRangeJson = command.Contract.TargetChapterRangeJson,
            SuccessCriteriaJson = JsonSerializer.Serialize(command.Contract.SuccessCriteria),
            MustPreserveJson = JsonSerializer.Serialize(command.Contract.MustPreserve),
            MustHappenJson = JsonSerializer.Serialize(command.Contract.MustHappen),
            MustNotChangeJson = JsonSerializer.Serialize(command.Contract.MustNotChange),
            AcceptancePolicyJson = command.Contract.AcceptancePolicyJson,
            ReworkPolicyJson = command.Contract.ReworkPolicyJson,
            ExecutionStrategy = BookExecutionStrategies.RequireValid(command.Contract.ExecutionStrategy),
            BookPlanJson = command.Contract.BookPlanJson,
            TotalCostLimit = command.TotalCostLimit,
            CanonBaselineVersion = frozen.CanonVersion,
            KnowledgeSnapshotVersion = frozen.KnowledgeVersion,
            QualityContractVersion = frozen.QualityContractVersion,
            StyleProfileVersion = frozen.StyleProfileVersion,
            ModelConfigVersionsJson = frozen.ModelConfigVersionsJson,
            ProtocolVersionsJson = frozen.ProtocolVersionsJson,
            Status = "committed",
            AggregateVersion = 1,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now
        };
        var snapshot = new GoalContextSnapshot
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = command.ProjectId,
            GoalId = goal.Id,
            CanonVersion = frozen.CanonVersion,
            KnowledgeVersion = frozen.KnowledgeVersion,
            QualityContractVersion = frozen.QualityContractVersion,
            StyleProfileVersion = frozen.StyleProfileVersion,
            ModelConfigVersionsJson = frozen.ModelConfigVersionsJson,
            ProtocolVersionsJson = frozen.ProtocolVersionsJson,
            ContentHashesJson = frozen.ContentHashesJson,
            CreatedAt = now
        };

        _db.CreativeGoals.Add(goal);
        _db.GoalContextSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(cancellationToken);
        await _bookProductions.InitializeAsync(goal, cancellationToken);
        return new GoalSubmissionResult(GoalSubmissionStatus.Created, goal.Id);
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
        var nextRevision = await _db.GoalRevisions
            .Where(item => item.UserId == userId && item.GoalId == goal.Id)
            .Select(item => (int?)item.RevisionNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var revision = new GoalRevision
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = goal.ProjectId,
            GoalId = goal.Id,
            RevisionNumber = nextRevision + 1,
            Reason = RequireText(command.Reason, nameof(command.Reason)),
            ConstraintChangesJson = JsonSerializer.Serialize(changes, JsonOptions),
            ReusableArtifactIdsJson = JsonSerializer.Serialize(command.ReusableArtifactIds),
            InvalidatedArtifactIdsJson = JsonSerializer.Serialize(command.InvalidatedArtifactIds),
            AffectedNodeIdsJson = JsonSerializer.Serialize(command.AffectedNodeIds.Distinct(StringComparer.Ordinal)),
            CreatedAt = DateTime.UtcNow
        };
        _db.GoalRevisions.Add(revision);
        await _db.SaveChangesAsync(cancellationToken);
        return revision;
    }

    private static bool ContractsMatch(CreativeGoalContract left, CreativeGoalContract right) =>
        JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("值不能为空。", parameterName)
            : value.Trim();
}

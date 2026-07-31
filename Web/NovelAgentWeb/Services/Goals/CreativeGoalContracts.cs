using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed record CreativeGoalContract(
    string GoalType,
    string CollaborationMode,
    string HumanReadableObjective,
    string TargetChapterRangeJson,
    IReadOnlyList<string> SuccessCriteria,
    IReadOnlyList<string> MustPreserve,
    IReadOnlyList<string> MustHappen,
    IReadOnlyList<string> MustNotChange,
    string AcceptancePolicyJson,
    string ReworkPolicyJson);

public sealed record CreateCreativeGoalCommand(
    string ProjectId,
    string SourceSessionId,
    string IdempotencyKey,
    decimal TotalCostLimit,
    CreativeGoalContract Contract);

public sealed record ReviseCreativeGoalCommand(
    string GoalId,
    string Reason,
    string ConstraintChangesJson,
    IReadOnlyList<string> ReusableArtifactIds,
    IReadOnlyList<string> InvalidatedArtifactIds,
    IReadOnlyList<string> AffectedNodeIds);

public sealed record GoalConstraintChanges(
    string? HumanReadableObjective = null,
    string? TargetChapterRangeJson = null,
    IReadOnlyList<string>? SuccessCriteria = null,
    IReadOnlyList<string>? MustPreserve = null,
    IReadOnlyList<string>? MustHappen = null,
    IReadOnlyList<string>? MustNotChange = null,
    string? AcceptancePolicyJson = null,
    string? ReworkPolicyJson = null,
    decimal? TotalCostLimit = null);

public enum GoalSubmissionStatus
{
    NotCommitted,
    NeedsConfirmation,
    Created,
    Existing
}

public sealed record GoalSubmissionResult(GoalSubmissionStatus Status, string? GoalId = null);

public sealed record GoalBaselines(
    string CanonVersion,
    string KnowledgeVersion,
    string QualityContractVersion,
    string StyleProfileVersion,
    string ModelConfigVersionsJson,
    string ProtocolVersionsJson,
    string ContentHashesJson);

public interface ICreativeGoalService
{
    Task<GoalSubmissionResult> SubmitAsync(
        CreateCreativeGoalCommand command,
        CommitmentAssessment assessment,
        CancellationToken cancellationToken = default);

    Task<GoalRevision> ReviseAsync(
        ReviseCreativeGoalCommand command,
        CancellationToken cancellationToken = default);
}

public interface IGoalBaselineProvider
{
    Task<GoalBaselines> CaptureAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken = default);
}

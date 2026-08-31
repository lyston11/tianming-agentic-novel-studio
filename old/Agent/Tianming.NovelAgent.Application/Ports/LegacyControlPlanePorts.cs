namespace Tianming.NovelAgent.Application.Ports;

public interface ILegacyControlPlaneCommands
{
    Task<LegacyGoalSubmissionResult> SubmitGoalAsync(
        LegacyGoalSubmissionCommand command,
        CancellationToken cancellationToken = default);

    Task PersistCompiledGraphAsync(
        LegacyCompiledGraphCommand command,
        CancellationToken cancellationToken = default);

    Task CreateArtifactAsync(
        LegacyArtifactCommand command,
        CancellationToken cancellationToken = default);

    Task<LegacyGoalRevisionResult> CreateGoalRevisionAsync(
        LegacyGoalRevisionCommand command,
        CancellationToken cancellationToken = default);

    Task<LegacyProductionState> ChangeExecutionStrategyAsync(
        LegacyExecutionStrategyCommand command,
        CancellationToken cancellationToken = default);

    Task<LegacyBatchState> ContinueInteractiveBatchAsync(
        LegacyWorkflowCommand command,
        CancellationToken cancellationToken = default);

    Task<LegacyBatchState?> BeginBatchAcceptanceAsync(
        LegacyBeginBatchAcceptanceCommand command,
        CancellationToken cancellationToken = default);

    Task<LegacyBatchAdvanceResult> FinalizeMergedBatchAsync(
        LegacyFinalizeMergedBatchCommand command,
        CancellationToken cancellationToken = default);

    Task BlockBatchAsync(
        LegacyBlockBatchCommand command,
        CancellationToken cancellationToken = default);

    Task ApplyTaskFailureAsync(
        LegacyTaskFailureCommand command,
        CancellationToken cancellationToken = default);

    Task<string> PauseGoalAsync(
        LegacyGoalPauseCommand command,
        CancellationToken cancellationToken = default);

    Task ResumeGoalAsync(
        LegacyGoalResumeCommand command,
        CancellationToken cancellationToken = default);

    Task CancelGoalAsync(
        LegacyGoalCancelCommand command,
        CancellationToken cancellationToken = default);

    Task<LegacySafePointResult> ReachSafePointAsync(
        LegacySafePointCommand command,
        CancellationToken cancellationToken = default);

    Task PersistReworkGraphAsync(
        LegacyReworkGraphCommand command,
        CancellationToken cancellationToken = default);
}

public static class LegacyControlPlaneCommands
{
    public static ILegacyControlPlaneCommands Unconfigured { get; } = new UnconfiguredCommands();

    private sealed class UnconfiguredCommands : ILegacyControlPlaneCommands
    {
        public Task<LegacyGoalSubmissionResult> SubmitGoalAsync(
            LegacyGoalSubmissionCommand command,
            CancellationToken cancellationToken = default) => Missing<LegacyGoalSubmissionResult>();

        public Task PersistCompiledGraphAsync(
            LegacyCompiledGraphCommand command,
            CancellationToken cancellationToken = default) => Missing();

        public Task CreateArtifactAsync(
            LegacyArtifactCommand command,
            CancellationToken cancellationToken = default) => Missing();

        public Task<LegacyGoalRevisionResult> CreateGoalRevisionAsync(
            LegacyGoalRevisionCommand command,
            CancellationToken cancellationToken = default) => Missing<LegacyGoalRevisionResult>();

        public Task<LegacyProductionState> ChangeExecutionStrategyAsync(
            LegacyExecutionStrategyCommand command,
            CancellationToken cancellationToken = default) => Missing<LegacyProductionState>();

        public Task<LegacyBatchState> ContinueInteractiveBatchAsync(
            LegacyWorkflowCommand command,
            CancellationToken cancellationToken = default) => Missing<LegacyBatchState>();

        public Task<LegacyBatchState?> BeginBatchAcceptanceAsync(
            LegacyBeginBatchAcceptanceCommand command,
            CancellationToken cancellationToken = default) => Missing<LegacyBatchState?>();

        public Task<LegacyBatchAdvanceResult> FinalizeMergedBatchAsync(
            LegacyFinalizeMergedBatchCommand command,
            CancellationToken cancellationToken = default) => Missing<LegacyBatchAdvanceResult>();

        public Task BlockBatchAsync(
            LegacyBlockBatchCommand command,
            CancellationToken cancellationToken = default) => Missing();

        public Task ApplyTaskFailureAsync(
            LegacyTaskFailureCommand command,
            CancellationToken cancellationToken = default) => Missing();

        public Task<string> PauseGoalAsync(
            LegacyGoalPauseCommand command,
            CancellationToken cancellationToken = default) => Missing<string>();

        public Task ResumeGoalAsync(
            LegacyGoalResumeCommand command,
            CancellationToken cancellationToken = default) => Missing();

        public Task CancelGoalAsync(
            LegacyGoalCancelCommand command,
            CancellationToken cancellationToken = default) => Missing();

        public Task<LegacySafePointResult> ReachSafePointAsync(
            LegacySafePointCommand command,
            CancellationToken cancellationToken = default) => Missing<LegacySafePointResult>();

        public Task PersistReworkGraphAsync(
            LegacyReworkGraphCommand command,
            CancellationToken cancellationToken = default) => Missing();

        private static Task Missing() => Task.FromException(Error());
        private static Task<T> Missing<T>() => Task.FromException<T>(Error());
        private static InvalidOperationException Error() =>
            new("Legacy control-plane mutations require an Application-owned command implementation.");
    }
}

public sealed record LegacyFrozenBaselines(
    string CanonVersion,
    string KnowledgeVersion,
    string QualityContractVersion,
    string StyleProfileVersion,
    string ModelConfigVersionsJson,
    string ProtocolVersionsJson,
    string ContentHashesJson);

public sealed record LegacyGoalSubmissionCommand(
    string UserId,
    string ProjectId,
    string SourceSessionId,
    string IdempotencyKey,
    decimal TotalCostLimit,
    string GoalType,
    string CollaborationMode,
    string HumanReadableObjective,
    string TargetChapterRangeJson,
    string SuccessCriteriaJson,
    string MustPreserveJson,
    string MustHappenJson,
    string MustNotChangeJson,
    string AcceptancePolicyJson,
    string ReworkPolicyJson,
    string ExecutionStrategy,
    string BookPlanJson,
    LegacyFrozenBaselines Baselines,
    string? GoalId = null,
    string? SnapshotId = null);

public sealed record LegacyGoalSubmissionResult(
    string GoalId,
    bool Existing);

public sealed record LegacyCompiledTask(
    string NodeId,
    string TaskType,
    string KernelName,
    string Status,
    string DependencyTaskIdsJson,
    string InputArtifactIdsJson,
    string OutputArtifactIdsJson,
    int MaxAttempts,
    int Priority,
    int? ChapterNumber,
    string? BranchId,
    bool Reused);

public sealed record LegacyCompiledGraphCommand(
    string UserId,
    string ProjectId,
    string GoalId,
    string? GoalRevisionId,
    string GraphId,
    int Version,
    string GraphJson,
    string ContentHash,
    string BranchId,
    int BatchStartChapterNumber,
    int BatchEndChapterNumber,
    IReadOnlyList<LegacyCompiledTask> Tasks,
    DateTimeOffset CreatedAt);

public sealed record LegacyArtifactCommand(
    string UserId,
    string ProjectId,
    string GoalId,
    string TaskId,
    string ArtifactId,
    string? BranchId,
    string ArtifactType,
    int SchemaVersion,
    string ContentJson,
    string ContentHash,
    string Status,
    string Authorship,
    bool IsProtected,
    string? ModelExecutionId,
    string? CausationId,
    DateTimeOffset CreatedAt,
    bool AttachToTaskOutput = false);

public sealed record LegacyGoalRevisionCommand(
    string UserId,
    string ProjectId,
    string GoalId,
    string RevisionId,
    int RevisionNumber,
    string Reason,
    string ConstraintChangesJson,
    string ReusableArtifactIdsJson,
    string InvalidatedArtifactIdsJson,
    string AffectedNodeIdsJson,
    DateTimeOffset CreatedAt,
    string? ConfirmationActorId = null,
    string? ConfirmationIdempotencyKey = null,
    string? SourceSessionId = null,
    string? SourceProposalId = null,
    string? PreviousRevisionId = null);

public sealed record LegacyGoalRevisionResult(
    string Id,
    string GoalId,
    int RevisionNumber,
    string Reason,
    string ConstraintChangesJson,
    string ReusableArtifactIdsJson,
    string InvalidatedArtifactIdsJson,
    string AffectedNodeIdsJson,
    DateTimeOffset CreatedAt);

public sealed record LegacyWorkflowCommand(
    string UserId,
    string GoalId);

public sealed record LegacyExecutionStrategyCommand(
    string UserId,
    string GoalId,
    string ExecutionStrategy);

public sealed record LegacyBeginBatchAcceptanceCommand(
    string UserId,
    string GoalId,
    string BatchId,
    string BranchId,
    string AcceptanceActor,
    DateTimeOffset? StaleAcceptingBefore = null);

public sealed record LegacyBlockBatchCommand(
    string UserId,
    string GoalId,
    string BatchId);

public sealed record LegacyTaskFailureCommand(
    string UserId,
    string GoalId,
    string GoalStatus,
    string ProductionStatus);

public sealed record LegacyGoalPauseCommand(
    string UserId,
    string GoalId);

public sealed record LegacyGoalResumeCommand(
    string UserId,
    string GoalId);

public sealed record LegacyGoalCancelCommand(
    string UserId,
    string GoalId,
    string? FinalBranchStatus,
    bool IdempotentRetry);

public sealed record LegacySafePointArtifact(
    string ArtifactType,
    int SchemaVersion,
    string ContentJson,
    string ContentHash,
    string Authorship,
    bool IsProtected);

public sealed record LegacySafePointCommand(
    string UserId,
    string ProjectId,
    string GoalId,
    string TaskId,
    string? BranchId,
    string LeaseOwner,
    IReadOnlyList<LegacySafePointArtifact> Artifacts);

public sealed record LegacySafePointResult(
    string Disposition,
    IReadOnlyList<string> ArtifactIds);

public sealed record LegacyReworkGraphIntentArtifact(
    string ArtifactId,
    string TaskId,
    string? BranchId,
    string ArtifactType,
    int SchemaVersion,
    string ContentJson,
    string ContentHash,
    string Status,
    string Authorship);

public sealed record LegacyReworkTask(
    string Id,
    string TaskType,
    string KernelName,
    string Status,
    string DependencyTaskIdsJson,
    string InputArtifactIdsJson,
    string OutputArtifactIdsJson,
    string IdempotencyKey,
    int MaxAttempts,
    int Priority);

public sealed record LegacyReworkGraphCommand(
    string UserId,
    string ProjectId,
    string GoalId,
    string GraphId,
    string UpdatedGraphJson,
    string UpdatedGraphContentHash,
    string DraftTaskId,
    string DraftInputArtifactIdsJson,
    LegacyReworkGraphIntentArtifact IntentArtifact,
    IReadOnlyList<LegacyReworkTask> Tasks);

public sealed record LegacyBookValidationArtifact(
    string ContentJson,
    string ContentHash,
    string Status);

public sealed record LegacyFinalizeMergedBatchCommand(
    string UserId,
    string GoalId,
    string BranchId,
    LegacyBookValidationArtifact? ValidationArtifact = null);

public sealed record LegacyProductionState(
    string Id,
    string UserId,
    string ProjectId,
    string GoalId,
    string ExecutionStrategy,
    string Status,
    int TargetStartChapterNumber,
    int TargetEndChapterNumber,
    int NextChapterNumber,
    int BatchSize,
    int CurrentBatchNumber,
    string CompletionCriteriaJson,
    string PausePolicyJson,
    long AggregateVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record LegacyBatchState(
    string Id,
    string UserId,
    string ProjectId,
    string GoalId,
    string BookProductionId,
    int BatchNumber,
    int StartChapterNumber,
    int EndChapterNumber,
    string Status,
    string? TaskGraphVersionId,
    string? CanonBranchId,
    string AcceptanceActor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record LegacyBatchAdvanceResult(
    LegacyProductionState Production,
    LegacyBatchState CompletedBatch,
    LegacyBatchState? NextBatch,
    bool ShouldCompileNextBatch);

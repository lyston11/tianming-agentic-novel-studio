namespace Tianming.NovelAgent.Infrastructure.Persistence;

public sealed class ConversationMessageRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ConversationTurnRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ResultJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ConversationRuntimeCheckpointRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Runtime { get; set; } = string.Empty;
    public string CheckpointJson { get; set; } = "{}";
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GoalProposalRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SourceSessionId { get; set; } = string.Empty;
    public string ContractJson { get; set; } = "{}";
    public string ContractHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? DecisionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ContextCheckpointRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ContractHash { get; set; } = string.Empty;
    public string CanonBaselineVersion { get; set; } = string.Empty;
    public string KnowledgeSnapshotVersion { get; set; } = string.Empty;
    public string StyleProfileVersion { get; set; } = string.Empty;
    public string ModelVersionsJson { get; set; } = "{}";
    public string PromptVersionsJson { get; set; } = "{}";
    public string SchemaVersionsJson { get; set; } = "{}";
    public string ProtocolVersionsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreativeGoalRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SourceSessionId { get; set; } = string.Empty;
    public string GoalType { get; set; } = "novel_production";
    public string CollaborationMode { get; set; } = "coauthor";
    public string HumanReadableObjective { get; set; } = string.Empty;
    public string TargetChapterRangeJson { get; set; } = "{}";
    public string SuccessCriteriaJson { get; set; } = "[]";
    public string MustPreserveJson { get; set; } = "[]";
    public string MustHappenJson { get; set; } = "[]";
    public string MustNotChangeJson { get; set; } = "[]";
    public string AcceptancePolicyJson { get; set; } = "{}";
    public string ReworkPolicyJson { get; set; } = "{}";
    public string ExecutionStrategy { get; set; } = "interactive_batch";
    public string BookPlanJson { get; set; } = "{}";
    public decimal TotalCostLimit { get; set; }
    public decimal ReservedCost { get; set; }
    public decimal ActualCost { get; set; }
    public string CanonBaselineVersion { get; set; } = string.Empty;
    public string KnowledgeSnapshotVersion { get; set; } = string.Empty;
    public string QualityContractVersion { get; set; } = string.Empty;
    public string StyleProfileVersion { get; set; } = string.Empty;
    public string ModelConfigVersionsJson { get; set; } = "{}";
    public string ProtocolVersionsJson { get; set; } = "{}";
    public string Status { get; set; } = "committed";
    public long AggregateVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? CurrentRevisionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GoalRevisionRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ConstraintChangesJson { get; set; } = "{}";
    public string ReusableArtifactIdsJson { get; set; } = "[]";
    public string InvalidatedArtifactIdsJson { get; set; } = "[]";
    public string AffectedNodeIdsJson { get; set; } = "[]";
    public string? TaskGraphVersionId { get; set; }
    public string ContractJson { get; set; } = "{}";
    public string ContextJson { get; set; } = "{}";
    public string ContractHash { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = "1";
    public string ConfirmationActorId { get; set; } = string.Empty;
    public DateTimeOffset ConfirmationTime { get; set; }
    public string ConfirmationIdempotencyKey { get; set; } = string.Empty;
    public string SourceSessionId { get; set; } = string.Empty;
    public string SourceProposalId { get; set; } = string.Empty;
    public string? PreviousRevisionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BookProductionRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string GoalRevisionId { get; set; } = string.Empty;
    public string ExecutionStrategy { get; set; } = "interactive_batch";
    public string Status { get; set; } = "planned";
    public int TargetStartChapterNumber { get; set; }
    public int TargetEndChapterNumber { get; set; }
    public int NextChapterNumber { get; set; }
    public int BatchSize { get; set; }
    public int CurrentBatchNumber { get; set; }
    public string CompletionCriteriaJson { get; set; } = "{}";
    public string PausePolicyJson { get; set; } = "{}";
    public string TaskGraphVersionId { get; set; } = string.Empty;
    public string? CanonLeaseOwner { get; set; }
    public DateTimeOffset? CanonLeaseExpiresAt { get; set; }
    public long? CanonLeaseFenceToken { get; set; }
    public string? TerminalReason { get; set; }
    public long AggregateVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ProductionBatchRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string BookProductionId { get; set; } = string.Empty;
    public int BatchNumber { get; set; }
    public int StartChapterNumber { get; set; }
    public int EndChapterNumber { get; set; }
    public string Status { get; set; } = "planned";
    public string? TaskGraphVersionId { get; set; }
    public string? CanonBranchId { get; set; }
    public string AcceptanceActor { get; set; } = "human";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class CanonBranchRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string CanonBaselineVersion { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public int StartChapterNumber { get; set; }
    public int EndChapterNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
}

public sealed class GoalContextSnapshotRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string CanonVersion { get; set; } = string.Empty;
    public string KnowledgeVersion { get; set; } = string.Empty;
    public string QualityContractVersion { get; set; } = string.Empty;
    public string StyleProfileVersion { get; set; } = string.Empty;
    public string ModelConfigVersionsJson { get; set; } = "{}";
    public string ProtocolVersionsJson { get; set; } = "{}";
    public string ContentHashesJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TaskGraphRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string? GoalRevisionId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = "active";
    public string GraphJson { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class KernelTaskRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string TaskGraphVersionId { get; set; } = string.Empty;
    public string? BranchId { get; set; }
    public string KernelName { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = "blocked";
    public string DependencyTaskIdsJson { get; set; } = "[]";
    public string InputArtifactIdsJson { get; set; } = "[]";
    public string OutputArtifactIdsJson { get; set; } = "[]";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string? FailureKind { get; set; }
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class KernelArtifactRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string? BranchId { get; set; }
    public string ArtifactType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string ContentJson { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public string Status { get; set; } = "proposed";
    public string Authorship { get; set; } = "agent";
    public bool IsProtected { get; set; }
    public string? ModelExecutionId { get; set; }
    public string? CausationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ModelExecutionRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string KernelName { get; set; } = string.Empty;
    public string ModelConfigVersionId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string OperationKey { get; set; } = string.Empty;
    public string Status { get; set; } = "reserved";
    public decimal ReservedCost { get; set; }
    public decimal ActualCost { get; set; }
    public string Currency { get; set; } = "USD";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? ProviderLeaseOwner { get; set; }
    public int Attempt { get; set; }
    public string? ResultJson { get; set; }
    public string? ResultContentHash { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class DomainEventRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string GoalId { get; set; } = string.Empty;
    public string? TaskId { get; set; }
    public string? BranchId { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public long AggregateVersion { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ArtifactRefsJson { get; set; } = "[]";
    public string EvidenceRefsJson { get; set; } = "[]";
    public string? CausationId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? ModelExecutionId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AgentStreamEventRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string StreamKind { get; set; } = string.Empty;
    public string StreamId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public string? SessionId { get; set; }
    public string? GoalId { get; set; }
    public string? ProductionId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class StreamSequenceRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string StreamKind { get; set; } = string.Empty;
    public string StreamId { get; set; } = string.Empty;
    public long NextSequence { get; set; } = 1;
    public long Version { get; set; }
}

public sealed class OutboxEventRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string? RuntimeRunId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? ProcessingOwner { get; set; }
    public DateTimeOffset? ProcessingLeaseExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? StreamEventId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CanonWriteLeaseRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProductionId { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public long FenceToken { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface INovelProductionStateQueryService
{
    Task<NovelProductionStateQueryResult?> QueryAsync(
        NovelProductionStateQueryRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record NovelProductionStateQueryRequest(
    string UserId,
    string SessionId,
    string ProjectId,
    string RunId,
    string ChapterId,
    int ChapterNumber,
    bool IncludeEvents);

public sealed class NovelProductionStateQueryResult
{
    public string ProjectId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public NovelRuntimeRunState? RuntimeRun { get; set; }
    public List<NovelRuntimeEventState> RuntimeEvents { get; set; } = new();
    public List<NovelProductionToolExecutionState> ToolExecutions { get; set; } = new();
    public List<NovelProductionPackageState> Packages { get; set; } = new();
    public List<NovelProductionRebuildLinkState> RebuildLinks { get; set; } = new();
    public List<NovelProductionChainState> ProductionChains { get; set; } = new();
    public List<NovelProductionStalePackageState> StalePackages { get; set; } = new();
    public List<ProductionDependencyBlock> DependencyBlocks { get; set; } = new();
    public List<NovelProductionRevisionPlanState> RevisionPlans { get; set; } = new();
    public List<NovelProductionEventState> ProductionEvents { get; set; } = new();
    public List<NovelProductionOutputArtifactState> OutputArtifacts { get; set; } = new();
    public List<NovelProductionOutboxState> OutboxEvents { get; set; } = new();
    public List<NovelProductionFactSnapshotState> FactSnapshots { get; set; } = new();
    public List<NovelProductionChapterDraftState> ChapterDrafts { get; set; } = new();
    public List<NovelProductionChapterChangeState> ChapterChanges { get; set; } = new();
    public List<NovelProductionGenerationGateReportState> GenerationGateReports { get; set; } = new();
    public List<NovelProductionAgentReviewState> AgentReviews { get; set; } = new();
    public List<NovelProductionMemoryReadState> MemoryReads { get; set; } = new();
    public List<NovelProductionMemoryPromotionState> MemoryPromotions { get; set; } = new();
}

public sealed class NovelRuntimeRunState
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string CurrentPhase { get; set; } = string.Empty;
    public int CurrentStep { get; set; }
    public string ActiveTool { get; set; } = string.Empty;
    public string LastMessage { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public NovelRuntimeRunFailureState? Failure { get; set; }
    public bool CancelRequested { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class NovelRuntimeRunFailureState
{
    public string Code { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Recoverable { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public List<string> ArtifactIds { get; set; } = new();
    public bool RequiresUserDecision { get; set; }
}

public sealed class NovelRuntimeEventState
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string DisplaySurface { get; set; } = string.Empty;
    public string DisplayPolicy { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionToolExecutionState
{
    public string Id { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string ResultMessage { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public NovelProductionToolFailureState? Failure { get; set; }
    public NovelProductionToolSemanticContractState SemanticContract { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class NovelProductionToolSemanticContractState
{
    public string DisplayName { get; set; } = string.Empty;
    public string DomainSurface { get; set; } = string.Empty;
    public string OutputKind { get; set; } = string.Empty;
    public List<string> InputArtifacts { get; set; } = new();
    public List<string> OutputArtifacts { get; set; } = new();
    public string IdempotencyPolicy { get; set; } = string.Empty;
    public string RollbackPolicy { get; set; } = string.Empty;
    public string UserVisibleWhere { get; set; } = string.Empty;
    public string ResultSemantics { get; set; } = string.Empty;
}

public sealed class NovelProductionToolFailureState
{
    public string Code { get; set; } = string.Empty;
    public string FailedStage { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool Recoverable { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public List<string> RecoverableActions { get; set; } = new();
    public List<NovelProductionToolFailureArtifactState> ProducedArtifacts { get; set; } = new();
    public List<NovelProductionToolInputArtifactState> InputArtifacts { get; set; } = new();
    public bool RequiresUserDecision { get; set; }
}

public sealed class NovelProductionToolFailureArtifactState
{
    public string ArtifactType { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed class NovelProductionToolInputArtifactState
{
    public string ArtifactName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool BlocksExecution { get; set; }
    public List<string> RecommendedActions { get; set; } = new();
}

public sealed class NovelProductionPackageState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string PackageKind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string KernelVersion { get; set; } = string.Empty;
    public List<string> RebuiltFromPackageIds { get; set; } = new();
    public NovelProductionKnowledgeBindingSummaryState KnowledgeBindingSummary { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionKnowledgeBindingSummaryState
{
    public int BindingCount { get; set; }
    public int ShouldEnterGateCount { get; set; }
    public int ShouldEnterBlueprintCount { get; set; }
    public int ShouldEnterFactSnapshotCount { get; set; }
    public int HardConstraintCount { get; set; }
    public int ReferenceCount { get; set; }
    public int ClassifiedCount { get; set; }
    public int PendingClassificationCount { get; set; }
    public int ImportedCount { get; set; }
    public int ReferencedCount { get; set; }
}

public sealed class NovelProductionRebuildLinkState
{
    public string OldPackageId { get; set; } = string.Empty;
    public string OldPackageStatus { get; set; } = string.Empty;
    public string NewPackageId { get; set; } = string.Empty;
    public string NewPackageStatus { get; set; } = string.Empty;
    public string NewPackageKind { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
}

public sealed class NovelProductionStalePackageState
{
    public string PackageId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string RevisionPlanId { get; set; } = string.Empty;
    public string RevisionPlanStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string RecommendedToolName { get; set; } = string.Empty;
    public Dictionary<string, string> RecommendedArguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class NovelProductionChainState
{
    public string Id { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string ChapterLogicalId { get; set; } = string.Empty;
    public string ChapterDisplayName { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ChapterVersionId { get; set; } = string.Empty;
    public int ChapterVersionNumber { get; set; }
    public string FactSnapshotId { get; set; } = string.Empty;
    public int FactSnapshotVersion { get; set; }
    public List<string> RevisionPlanIds { get; set; } = new();
    public List<NovelProductionRebuildLinkState> RebuildLinks { get; set; } = new();
    public WorkflowProductionChainEvidence Evidence { get; set; } =
        new(null, null, null, 0, Array.Empty<string>());
    public List<NovelProductionChainStepState> Steps { get; set; } = new();
}

public sealed class NovelProductionChainStepState
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string OutboxEventId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionRevisionPlanState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string CreativeIntentId { get; set; } = string.Empty;
    public string KnowledgeConflictReportId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public string TargetScope { get; set; } = string.Empty;
    public string TargetChapterId { get; set; } = string.Empty;
    public string TargetChapterLogicalId { get; set; } = string.Empty;
    public string TargetChapterDisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RequirementsJson { get; set; } = "[]";
    public string ImpactAnalysisJson { get; set; } = "{}";
    public string AffectedChapterIdsJson { get; set; } = "[]";
    public string InvalidatedPackageIdsJson { get; set; } = "[]";
    public string RiskLevel { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionEventState
{
    public string Id { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string DataJson { get; set; } = string.Empty;
    public NovelProductionEventFailureState? Failure { get; set; }
    public WorkflowGateEvidence? GateEvidence { get; set; }
    public WorkflowFactSnapshotEvidence? FactSnapshotEvidence { get; set; }
    public WorkflowAgentReviewSummaryEvidence? AgentReviewEvidence { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionEventFailureState
{
    public string Code { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Recoverable { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public List<string> ArtifactIds { get; set; } = new();
    public bool RequiresUserDecision { get; set; }
}

public sealed class NovelProductionOutputArtifactState
{
    public string Id { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string OutputKind { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> UserVisibleWhere { get; set; } = new();
    public bool VisibleInWorkflow { get; set; }
    public bool VisibleInLibrary { get; set; }
    public string SourceEventType { get; set; } = string.Empty;
    public string SourceEventId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionOutboxState
{
    public string Id { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime? NextAttemptAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionFactSnapshotState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string ChapterVersionId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ChapterTitle { get; set; } = string.Empty;
    public string ProtagonistName { get; set; } = string.Empty;
    public string ProtagonistIdentity { get; set; } = string.Empty;
    public string ProtagonistStatus { get; set; } = string.Empty;
    public string CurrentLocation { get; set; } = string.Empty;
    public string SystemState { get; set; } = string.Empty;
    public string EquipmentState { get; set; } = string.Empty;
    public List<string> KeyEvents { get; set; } = new();
    public string EndingState { get; set; } = string.Empty;
    public List<string> NextChapterMustCarry { get; set; } = new();
    public bool IsParseable { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionChapterChangeState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string ParseStatus { get; set; } = string.Empty;
    public string ParseError { get; set; } = string.Empty;
    public bool AppliedToFactSnapshot { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string ChangesJson { get; set; } = string.Empty;
    public string CanonicalChangesJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionChapterDraftState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ContentLength { get; set; }
    public int RepairAttemptCount { get; set; }
    public bool HasChanges { get; set; }
    public string Preview { get; set; } = string.Empty;
    public string ChangesJson { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionGenerationGateReportState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool ProtocolPassed { get; set; }
    public bool ChangesDetected { get; set; }
    public bool FactSnapshotPassed { get; set; }
    public bool BlueprintPassed { get; set; }
    public bool RagPassed { get; set; }
    public int IssueCount { get; set; }
    public int RepairHintCount { get; set; }
    public string ReportJson { get; set; } = string.Empty;
    public DateTime ValidatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionAgentReviewState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string ReviewId { get; set; } = string.Empty;
    public string OverallResult { get; set; } = string.Empty;
    public string ValidationOverallResult { get; set; } = string.Empty;
    public bool RequiresRewrite { get; set; }
    public int QualityScore { get; set; }
    public int ContentLength { get; set; }
    public int CheckCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public bool MeetsAcceptedCreativeIntents { get; set; } = true;
    public string ContinuityRisk { get; set; } = string.Empty;
    public string ChapterPacing { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string ReviewJson { get; set; } = string.Empty;
    public DateTime ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionMemoryReadState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string MemoryScope { get; set; } = string.Empty;
    public List<string> MemoryKeys { get; set; } = new();
    public string SourceType { get; set; } = string.Empty;
    public string Consumer { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class NovelProductionMemoryPromotionState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string SourceScope { get; set; } = string.Empty;
    public string TargetScope { get; set; } = string.Empty;
    public string SourceMemoryKey { get; set; } = string.Empty;
    public string TargetMemoryKey { get; set; } = string.Empty;
    public string PromotionReason { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

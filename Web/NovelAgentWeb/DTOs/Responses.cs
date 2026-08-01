using System.Text.Json.Serialization;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.DTOs;

public sealed record AgentChatResponse(
    string Reply,
    IReadOnlyList<string> Suggestions,
    string SessionId = "",
    string? RunId = null,
    string Phase = "",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentDecisionTrace? Decision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentRagContext? Rag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentWorkingMemorySnapshot? Memory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<AgentRuntimeStep>? RuntimeTrace = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentMissionPlan? MissionPlan = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentPendingConfirmation? PendingConfirmation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentMemoryAuditSummary? MemoryAudit = null,
    string ActiveProjectId = "",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DirectorTurnView? Director = null);

public static class AgentChatResponsePublicProjection
{
    public static AgentChatResponse ToPublic(AgentChatResponse response)
    {
        var activeProjectId = response.ActiveProjectId;
        if (string.IsNullOrWhiteSpace(activeProjectId))
            activeProjectId = response.MissionPlan?.ProjectId
                ?? response.Memory?.MissionPlan?.ProjectId
                ?? string.Empty;

        return new AgentChatResponse(
            response.Reply,
            response.Suggestions,
            response.SessionId,
            response.RunId,
            response.Phase,
            PendingConfirmation: response.PendingConfirmation ?? response.Memory?.PendingConfirmation,
            MemoryAudit: response.MemoryAudit,
            ActiveProjectId: activeProjectId,
            Director: response.Director);
    }
}

public sealed record AgentMemoryAuditSummary(
    IReadOnlyList<AgentMemoryReadAuditSummary> Reads,
    IReadOnlyList<AgentMemoryPromotionAuditSummary> Promotions);

public sealed record AgentMemoryReadAuditSummary(
    string Id,
    string ProjectId,
    string SessionId,
    string RunId,
    string MemoryScope,
    IReadOnlyList<string> MemoryKeys,
    string SourceType,
    string Consumer,
    DateTime CreatedAt);

public sealed record AgentMemoryPromotionAuditSummary(
    string Id,
    string ProjectId,
    string SessionId,
    string RunId,
    string SourceScope,
    string TargetScope,
    string SourceMemoryKey,
    string TargetMemoryKey,
    string PromotionReason,
    DateTime CreatedAt);

public sealed record NovelLibraryDocument(
    IReadOnlyList<NovelBookView> Books,
    NovelBookView? ActiveBook,
    IReadOnlyList<NovelVolumeView> Volumes,
    NovelChapterView? SelectedChapter,
    int GeneratedChapterCount,
    int PlannedChapterCount,
    int NeedsRewriteCount);

public sealed record NovelBookView(
    string ProjectId,
    string Title,
    string Genre,
    string SubGenre,
    string CoreHook,
    string ReaderPromise,
    string Status,
    bool IsActive,
    int VolumeCount,
    int GeneratedChapterCount,
    int PlannedChapterCount,
    int NeedsRewriteCount,
    string UpdatedAt,
    NovelChapterView? SelectedChapter);

public sealed record NovelVolumeView(
    string VolumeId,
    string Title,
    string Status,
    string StartChapterId,
    string EndChapterId,
    int ExpectedChapterCount,
    IReadOnlyList<NovelChapterView> Chapters);

public sealed record NovelChapterView(
    string ChapterId,
    string Title,
    string VolumeId,
    string VolumeTitle,
    int BeatIndex,
    string BeatRole,
    string Goal,
    string Turn,
    string Cost,
    string Status,
    string RunId,
    string Intent,
    string UpdatedAt,
    bool HasGeneratedContent,
    bool NeedsRewrite,
    int WordCount,
    string Summary,
    string Content,
    string SelectedCandidateTitle,
    int QualityScore,
    int RewriteAttemptCount,
    IReadOnlyList<string> ReviewChecks,
    IReadOnlyList<string> NextSuggestions,
    string WritingStatus,
    string ContextPackageStatus,
    string DraftArtifactStatus,
    string GateStatus,
    bool ChangesProtocolPassed,
    bool FactSnapshotPassed,
    bool BlueprintPassed,
    bool LongDistanceRagPassed,
    int RagRecallCount,
    int RepairAttemptCount,
    IReadOnlyList<string> GateIssues,
    IReadOnlyList<string> RepairHints,
    IReadOnlyList<string> DependencyWarnings,
    IReadOnlyList<string> ContextWarnings,
    bool VisibleInWorkflow,
    bool VisibleInLibrary,
    string UserVisibleStatus,
    string ArtifactStatus,
    string DraftArtifactId,
    string GateReportId,
    string QualityReportId,
    WorkflowChapterProductionSummary? ProductionSummary = null);

public sealed record WorkflowChapterProductionSummary(
    string ChainId,
    string Status,
    string Summary,
    string RuntimeRunId,
    string PackageId,
    string UpdatedAt,
    string ChapterVersionId,
    int ChapterVersionNumber,
    string FactSnapshotId,
    int FactSnapshotVersion,
    string EndingState,
    string GateStatus,
    string AgentReviewResult,
    string AgentReviewAction,
    int ChapterChangeCount,
    int RevisionPlanCount,
    int RebuildCount,
    IReadOnlyList<string> RevisionPlanIds,
    IReadOnlyList<WorkflowChapterRevisionPlanSummary> RevisionPlans,
    IReadOnlyList<string> RebuildPackageIds,
    IReadOnlyList<WorkflowChapterCreativeIntentSummary> CreativeIntents,
    IReadOnlyList<WorkflowChapterProductionTraceItem> TraceItems,
    bool HasCanonicalEvidence);

public sealed record WorkflowChapterRevisionPlanSummary(
    string RevisionPlanId,
    string Source,
    string PlanType,
    string TargetScope,
    string TargetChapterId,
    string TargetChapterLogicalId,
    string TargetChapterDisplayName,
    string Status,
    string RiskLevel,
    string Recommendation,
    IReadOnlyList<string> AffectedChapterIds,
    IReadOnlyList<string> InvalidatedPackageIds);

public sealed record WorkflowChapterCreativeIntentSummary(
    string IntentId,
    string NormalizedIntent,
    string TargetScope,
    string TargetChapterId,
    string ImpactLevel,
    string Source,
    string Status);

public sealed record WorkflowChapterProductionTraceItem(
    string Key,
    string Label,
    string Status,
    string ArtifactType,
    string ArtifactId,
    string Description,
    IReadOnlyList<string> RelatedArtifactIds);

public sealed record ProjectWorkflowDocument(
    NovelBookView? Project,
    NovelLibraryDocument Library,
    IReadOnlyList<WorkflowSessionSummary> Sessions,
    IReadOnlyList<WorkflowRunSummary> Runs,
    IReadOnlyList<AgentScheduledTask> SchedulerTasks,
    IReadOnlyList<AgentMissionPlan> MissionPlans,
    IReadOnlyList<WorkflowChapterArtifactSummary> ChapterArtifacts,
    IReadOnlyList<WorkflowChapterArtifactSummary> CurrentChapterArtifacts,
    IReadOnlyList<AgentScheduledTask> DiagnosticTasks,
    int ActivityScore,
    bool IsEmptyProject,
    IReadOnlyList<string> SuspectReasons,
    IReadOnlyList<string> StaleMissionWarnings,
    AgentPendingConfirmation? PendingConfirmation,
    string PendingConfirmationSessionId,
    string ActiveSessionId,
    string ActiveRunId,
    string UpdatedAt,
    IReadOnlyList<WorkflowCreativeIntentEvidence> CreativeIntents,
    IReadOnlyList<WorkflowProductionStage> ProductionStages,
    IReadOnlyList<WorkflowProductionChain> ProductionChains,
    IReadOnlyList<WorkflowArtifactTimelineItem> ArtifactTimeline)
{
    public string LatestGoalId { get; init; } = string.Empty;

    [JsonIgnore]
    public IReadOnlyList<NovelAgentRun> RawRuns { get; init; } = Array.Empty<NovelAgentRun>();
}

public sealed record WorkflowRunSummary(
    string RunId,
    string UserGoal,
    string Intent,
    string Status,
    string TargetChapterId,
    string CreatedAt,
    string UpdatedAt,
    string SelectedCandidateTitle,
    string DraftStatus,
    string GateStatus,
    string ReviewStatus,
    int QualityScore,
    IReadOnlyList<string> Notes,
    IReadOnlyList<WorkflowRunStepSummary> Steps);

public sealed record WorkflowRunStepSummary(
    string Id,
    string Name,
    string Purpose,
    string ToolName,
    string Status,
    string RiskLevel,
    bool RequiresConfirmation);

public sealed record WorkflowSessionSummary(
    string SessionId,
    string Title,
    string Phase,
    string ActiveRunId,
    string UpdatedAt,
    AgentMissionPlan MissionPlan,
    AgentPendingConfirmation? PendingConfirmation);

public sealed record WorkflowChapterArtifactSummary(
    string ChapterId,
    string RunId,
    string Intent,
    string Status,
    string UpdatedAt,
    string CandidateTitle,
    string DraftStatus,
    string DraftPreview,
    bool HasDraft,
    string GateStatus,
    IReadOnlyList<string> GateIssues,
    IReadOnlyList<string> RepairHints,
    string QualityStatus,
    int QualityScore,
    IReadOnlyList<string> QualityIssues,
    IReadOnlyList<string> ContextWarnings,
    IReadOnlyList<string> DependencyWarnings,
    int RagRecallCount,
    IReadOnlyList<string> SourceRunIds,
    int LifecycleRank,
    bool IsCurrent);

public sealed record WorkflowProductionStage(
    string Key,
    string Label,
    string Surface,
    string Status,
    string Summary,
    string Detail,
    int ArtifactCount,
    int CurrentCount,
    int TotalCount,
    string UpdatedAt,
    string PrimaryArtifactId,
    string PrimaryRunId,
    string EmptyReason,
    string NextIntentHint,
    IReadOnlyList<WorkflowProductionEventSummary> ProductionEvents)
{
    public IReadOnlyList<WorkflowToolExecutionSummary> ToolExecutions { get; init; } = Array.Empty<WorkflowToolExecutionSummary>();
}

public sealed record WorkflowToolExecutionSummary
{
    public string Id { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Risk { get; init; } = string.Empty;
    public string ResultMessage { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string StartedAt { get; init; } = string.Empty;
    public string CompletedAt { get; init; } = string.Empty;
    public WorkflowToolSemanticContractSummary SemanticContract { get; init; } = new();
    public WorkflowToolFailureSummary? Failure { get; init; }
}

public sealed record WorkflowToolSemanticContractSummary
{
    public string DisplayName { get; init; } = string.Empty;
    public string DomainSurface { get; init; } = string.Empty;
    public string OutputKind { get; init; } = string.Empty;
    public List<string> InputArtifacts { get; init; } = new();
    public List<string> OutputArtifacts { get; init; } = new();
    public string IdempotencyPolicy { get; init; } = string.Empty;
    public string RollbackPolicy { get; init; } = string.Empty;
    public string UserVisibleWhere { get; init; } = string.Empty;
    public string ResultSemantics { get; init; } = string.Empty;
}

public sealed record WorkflowToolFailureSummary
{
    public string Code { get; init; } = string.Empty;
    public string FailedStage { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool Recoverable { get; init; }
    public string RecommendedAction { get; init; } = string.Empty;
    public IReadOnlyList<WorkflowToolInputArtifactSummary> InputArtifacts { get; init; } =
        Array.Empty<WorkflowToolInputArtifactSummary>();
}

public sealed record WorkflowToolInputArtifactSummary(
    string ArtifactName,
    string Status,
    string ArtifactId,
    string Message,
    bool BlocksExecution,
    IReadOnlyList<string> RecommendedActions);

public sealed record WorkflowProductionChain(
    string Id,
    string ChapterId,
    string ChapterLogicalId,
    string ChapterDisplayName,
    string RuntimeRunId,
    string PackageId,
    string Status,
    string Summary,
    string UpdatedAt,
    string ChapterVersionId,
    int ChapterVersionNumber,
    string FactSnapshotId,
    int FactSnapshotVersion,
    IReadOnlyList<string> RevisionPlanIds,
    IReadOnlyList<WorkflowPackageRebuildLinkEvidence> RebuildLinks,
    IReadOnlyList<WorkflowProductionChainStep> Steps,
    WorkflowProductionChainEvidence Evidence);

public sealed record WorkflowProductionChainEvidence(
    WorkflowGateEvidence? Gate,
    WorkflowFactSnapshotEvidence? FactSnapshot,
    WorkflowAgentReviewSummaryEvidence? AgentReview,
    int ChapterChangeCount,
    IReadOnlyList<string> ChapterChangeArtifactIds);

public sealed record WorkflowProductionChainStep(
    string Key,
    string Label,
    string Status,
    string EventId,
    string EventType,
    string Stage,
    string ArtifactType,
    string ArtifactId,
    string Message,
    string CreatedAt,
    string OutboxEventId);

public sealed record WorkflowProductionEventSummary(
    string Id,
    string RuntimeRunId,
    string ChapterId,
    string PackageId,
    string EventType,
    string Stage,
    string Status,
    string Message,
    string ArtifactType,
    string ArtifactId,
    string DataJson,
    string CreatedAt,
    WorkflowProductionEvidenceSummary? Evidence = null,
    WorkflowProductionFailureSummary? Failure = null);

public sealed record WorkflowProductionFailureSummary(
    string Code,
    string Stage,
    string Message,
    bool Recoverable,
    string RecommendedAction,
    IReadOnlyList<string> ArtifactIds,
    bool RequiresUserDecision);

public sealed record WorkflowProductionEvidenceSummary(
    string PackageKind,
    string PackageStatus,
    string PromptVersion,
    string KernelVersion,
    int KnowledgeFactCount,
    int RagQueryCount,
    int FactSnapshotVersion,
    string FactSnapshotSource,
    string FactSnapshotId,
    int ChapterVersionNumber,
    string ChapterVersionId,
    string ChapterVersionStatus,
    int ChapterVersionWordCount,
    int AcceptedCreativeIntentCount,
    IReadOnlyList<WorkflowKnowledgeBindingEvidence> KnowledgeBindings,
    IReadOnlyList<WorkflowCreativeIntentEvidence> CreativeIntents,
    IReadOnlyList<WorkflowAgentReviewCheckEvidence> AgentReviewChecks,
    IReadOnlyList<WorkflowKnowledgeConstraintEvidence> KnowledgeConstraintEvidence)
{
    public int KnowledgeBindingCount => KnowledgeBindings.Count;
    public IReadOnlyList<WorkflowRevisionPlanEvidence> SourceRevisionPlans { get; init; } =
        Array.Empty<WorkflowRevisionPlanEvidence>();
    public IReadOnlyList<string> RebuiltFromPackageIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<WorkflowPackageRebuildLinkEvidence> RebuildLinks { get; init; } =
        Array.Empty<WorkflowPackageRebuildLinkEvidence>();
    public WorkflowOutboxEvidence? Outbox { get; init; }
    public WorkflowRollbackEvidence? Rollback { get; init; }
    public WorkflowGateEvidence? Gate { get; init; }
    public WorkflowFactSnapshotEvidence? FactSnapshot { get; init; }
    public WorkflowAgentReviewSummaryEvidence? AgentReview { get; init; }
    public WorkflowKnowledgeBindingSummaryEvidence? KnowledgeBindingSummary { get; init; }
    public IReadOnlyList<WorkflowMemoryReadEvidence> MemoryReads { get; init; } =
        Array.Empty<WorkflowMemoryReadEvidence>();
    public IReadOnlyList<WorkflowMemoryPromotionEvidence> MemoryPromotions { get; init; } =
        Array.Empty<WorkflowMemoryPromotionEvidence>();
}

public sealed record WorkflowGateEvidence(
    string Status,
    bool ProtocolPassed,
    bool FactSnapshotPassed,
    bool BlueprintPassed,
    bool RagPassed,
    bool ChangesDetected,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> RepairHints);

public sealed record WorkflowFactSnapshotEvidence(
    string ProtagonistName,
    string ProtagonistIdentity,
    string ProtagonistStatus,
    string CurrentLocation,
    string SystemState,
    string EquipmentState,
    IReadOnlyList<string> KeyEvents,
    string EndingState,
    IReadOnlyList<string> NextChapterMustCarry);

public sealed record WorkflowAgentReviewSummaryEvidence(
    string Decision,
    string OverallResult,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Suggestions,
    bool? MeetsAcceptedCreativeIntents,
    string ContinuityRisk,
    string ChapterPacing,
    string RecommendedAction);

public sealed record WorkflowMemoryReadEvidence(
    string Id,
    string ProjectId,
    string SessionId,
    string RunId,
    string MemoryScope,
    IReadOnlyList<string> MemoryKeys,
    string SourceType,
    string Consumer,
    string CreatedAt);

public sealed record WorkflowMemoryPromotionEvidence(
    string Id,
    string ProjectId,
    string SessionId,
    string RunId,
    string SourceScope,
    string TargetScope,
    string SourceMemoryKey,
    string TargetMemoryKey,
    string PromotionReason,
    string CreatedAt);

public sealed record WorkflowOutboxEvidence(
    string OutboxEventId,
    string EventType,
    string AggregateType,
    string AggregateId,
    string Status,
    int Attempts,
    string LastError);

public sealed record WorkflowPackageRebuildLinkEvidence(
    string OldPackageId,
    string OldPackageStatus,
    string NewPackageId,
    string NewPackageStatus,
    string NewPackageKind,
    string ChapterId,
    string RuntimeRunId);

public sealed record WorkflowRollbackEvidence(
    string TargetVersionId,
    int TargetVersionNumber,
    string CurrentDocumentId,
    IReadOnlyList<string> InvalidatedPackageIds,
    string Reason);

public sealed record WorkflowRevisionPlanEvidence(
    string RevisionPlanId,
    string PlanType,
    string TargetScope,
    string TargetChapterId,
    string TargetChapterLogicalId,
    string TargetChapterDisplayName,
    string Status,
    IReadOnlyList<string> AffectedChapterIds,
    IReadOnlyList<string> InvalidatedPackageIds,
    string RiskLevel,
    string Recommendation);

public sealed record WorkflowKnowledgeBindingEvidence(
    string KnowledgeId,
    string Title,
    string EntryType,
    string ProjectUsageStatus,
    int Weight)
{
    public string Role { get; init; } = "";
    public string ConstraintLevel { get; init; } = "";
    public string PackagePolicy { get; init; } = "";
    public string ClassificationId { get; init; } = "";
    public string ClassificationRule { get; init; } = "";
    public bool ShouldEnterGate { get; init; }
    public bool ShouldEnterBlueprint { get; init; }
    public bool ShouldEnterFactSnapshot { get; init; }
    public double ClassificationConfidence { get; init; }
}

public sealed record WorkflowKnowledgeBindingSummaryEvidence(
    int BindingCount,
    int ShouldEnterGateCount,
    int ShouldEnterBlueprintCount,
    int ShouldEnterFactSnapshotCount,
    int HardConstraintCount,
    int ReferenceCount,
    int ClassifiedCount,
    int PendingClassificationCount,
    int ImportedCount,
    int ReferencedCount);

public sealed record WorkflowCreativeIntentEvidence(
    string IntentId,
    string NormalizedIntent,
    string TargetScope,
    string TargetChapterId,
    string ImpactLevel,
    string Source,
    string Status);

public sealed record WorkflowAgentReviewCheckEvidence(
    string Key,
    string Name,
    string Status,
    string Message,
    IReadOnlyList<string> Evidence);

public sealed record WorkflowKnowledgeConstraintEvidence(
    string KnowledgeId,
    string Title,
    string EntryType,
    string Subject,
    string ConstraintLevel,
    string PackagePolicy,
    string EvidenceStatus,
    string GateStatus,
    string ChapterId,
    string FactSnapshotId,
    int FactSnapshotVersion,
    IReadOnlyList<string> AllowedTerms,
    IReadOnlyList<string> ForbiddenTerms,
    IReadOnlyList<string> Violations)
{
    public string ClassificationId { get; init; } = "";
    public string ClassificationRule { get; init; } = "";
    public bool ShouldEnterGate { get; init; }
    public bool ShouldEnterBlueprint { get; init; }
    public bool ShouldEnterFactSnapshot { get; init; }
}

public sealed record WorkflowArtifactTimelineItem(
    string Id,
    string Kind,
    string Label,
    string Surface,
    string Status,
    string Title,
    string Summary,
    string Preview,
    string VolumeId,
    string ChapterId,
    string RunId,
    string UpdatedAt,
    bool IsFinal,
    bool IsUserVisible,
    string Source);

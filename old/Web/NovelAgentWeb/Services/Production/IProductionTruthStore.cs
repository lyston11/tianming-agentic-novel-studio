using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IProductionTruthStore
{
    Task<ChapterVersion> CreateChapterVersionAsync(CreateChapterVersionRequest request, CancellationToken cancellationToken = default);
    Task<ChapterVersion> AttachLatestChapterVersionToRunAsync(
        string projectId,
        string chapterId,
        string runtimeRunId,
        string? PackageId,
        string? GateReportJson,
        string? AgentReviewJson,
        CancellationToken cancellationToken = default);
    Task<TianmingPackage> CreatePackageAsync(CreateTianmingPackageRequest request, CancellationToken cancellationToken = default);
    Task<TianmingPackage?> GetPackageAsync(string projectId, string runtimeRunId, string packageId, CancellationToken cancellationToken = default);
    Task<bool> PackageExistsAsync(string projectId, string runtimeRunId, string packageId, CancellationToken cancellationToken = default);
    Task<ProductionEvent> AppendEventAsync(CreateProductionEventRequest request, CancellationToken cancellationToken = default);
    Task<ChapterDraft> CreateChapterDraftAsync(CreateChapterDraftRequest request, CancellationToken cancellationToken = default);
    Task<ChapterChange> CreateChapterChangeAsync(CreateChapterChangeRequest request, CancellationToken cancellationToken = default);
    Task<GenerationGateReportRecord> CreateGenerationGateReportAsync(CreateGenerationGateReportRequest request, CancellationToken cancellationToken = default);
    Task<AgentReviewRecord> CreateAgentReviewAsync(CreateAgentReviewRequest request, CancellationToken cancellationToken = default);
    Task<int> MarkChapterChangesAppliedToFactSnapshotAsync(MarkChapterChangesAppliedToFactSnapshotRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductionEvent>> GetEventsForRunAsync(string runtimeRunId, int limit = 200, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductionEvent>> GetEventsForChapterAsync(string projectId, string chapterId, int limit = 200, CancellationToken cancellationToken = default);
    Task<ProjectFactSnapshot> SaveFactSnapshotAsync(SaveProjectFactSnapshotRequest request, CancellationToken cancellationToken = default);
    Task<ProjectFactSnapshot?> GetLatestFactSnapshotAsync(string projectId, string? chapterId = null, CancellationToken cancellationToken = default);
    Task<OutboxEvent> EnqueueOutboxAsync(EnqueueOutboxEventRequest request, CancellationToken cancellationToken = default);
    Task MarkOutboxCompletedAsync(string outboxEventId, CancellationToken cancellationToken = default);
    Task MarkOutboxFailedAsync(string outboxEventId, string error, bool retryable, DateTime? nextAttemptAt = null, CancellationToken cancellationToken = default);
}

public sealed record CreateChapterVersionRequest(
    string UserId,
    string ProjectId,
    string ChapterId,
    string ContentDocumentId,
    string Title,
    int WordCount,
    string Status,
    string? RuntimeRunId,
    string? PackageId,
    string? GateReportJson,
    string? AgentReviewJson);

public sealed record CreateTianmingPackageRequest(
    string Id,
    string UserId,
    string ProjectId,
    string? ChapterId,
    string? RuntimeRunId,
    string PackageKind,
    string InputJson,
    string? DependencyVersionsJson,
    string? KnowledgeSnapshotJson,
    string? FactSnapshotJson,
    string? PromptVersion,
    string? KernelVersion);

public sealed record CreateProductionEventRequest(
    string RuntimeRunId,
    string UserId,
    string ProjectId,
    string? ChapterId,
    string? PackageId,
    string EventType,
    string Stage,
    string Status,
    string Message,
    string? ArtifactType,
    string? ArtifactId,
    string? DataJson);

public sealed record CreateChapterDraftRequest(
    string UserId,
    string ProjectId,
    string RuntimeRunId,
    string ChapterId,
    string? PackageId,
    string ArtifactId,
    string Status,
    string DraftContent,
    string? ChangesJson,
    int RepairAttemptCount,
    bool HasChanges);

public sealed record CreateChapterChangeRequest(
    string UserId,
    string ProjectId,
    string RuntimeRunId,
    string ChapterId,
    string? PackageId,
    string ChangesJson,
    string CanonicalChangesJson,
    string ParseStatus,
    string? ParseError,
    bool AppliedToFactSnapshot);

public sealed record CreateGenerationGateReportRequest(
    string UserId,
    string ProjectId,
    string RuntimeRunId,
    string ChapterId,
    string? PackageId,
    string ArtifactId,
    string Status,
    string ReportJson,
    bool ProtocolPassed,
    bool ChangesDetected,
    bool FactSnapshotPassed,
    bool BlueprintPassed,
    bool RagPassed,
    int IssueCount,
    int RepairHintCount,
    DateTime ValidatedAt);

public sealed record CreateAgentReviewRequest(
    string UserId,
    string ProjectId,
    string RuntimeRunId,
    string ChapterId,
    string? PackageId,
    string ReviewId,
    string OverallResult,
    string ValidationOverallResult,
    bool RequiresRewrite,
    int QualityScore,
    int ContentLength,
    int CheckCount,
    string Summary,
    string ReviewJson,
    DateTime ReviewedAt,
    bool MeetsAcceptedCreativeIntents = true,
    string ContinuityRisk = "",
    string ChapterPacing = "",
    string RecommendedAction = "");

public sealed record MarkChapterChangesAppliedToFactSnapshotRequest(
    string UserId,
    string ProjectId,
    string RuntimeRunId,
    string ChapterId,
    string? PackageId);

public sealed record SaveProjectFactSnapshotRequest(
    string UserId,
    string ProjectId,
    string? ChapterId,
    string? ChapterVersionId,
    string SnapshotJson,
    string Source);

public sealed record EnqueueOutboxEventRequest(
    string UserId,
    string? ProjectId,
    string? RuntimeRunId,
    string EventType,
    string AggregateType,
    string AggregateId,
    string PayloadJson,
    string? IdempotencyKey = null);

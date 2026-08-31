using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IProductionEventWriter
{
    Task<ProductionEvent> AppendChapterStageAsync(
        AppendChapterProductionEventRequest request,
        CancellationToken cancellationToken = default);

    Task<ChapterDraft> AppendChapterDraftAsync(
        AppendChapterDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<ChapterChange> AppendChapterChangeAsync(
        AppendChapterChangeRequest request,
        CancellationToken cancellationToken = default);

    Task<GenerationGateReportRecord> AppendGenerationGateReportAsync(
        AppendGenerationGateReportRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentReviewRecord> AppendAgentReviewAsync(
        AppendAgentReviewRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AppendChapterProductionEventRequest(
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
    object? Data);

public sealed record AppendChapterDraftRequest(
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

public sealed record AppendChapterChangeRequest(
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

public sealed record AppendGenerationGateReportRequest(
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

public sealed record AppendAgentReviewRequest(
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

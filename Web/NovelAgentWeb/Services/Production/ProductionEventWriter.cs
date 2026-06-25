using System.Text.Json;
using System.Text.Encodings.Web;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionEventWriter : IProductionEventWriter
{
    private static readonly JsonSerializerOptions EventJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IProductionTruthStore _truthStore;
    private readonly NovelAgentDbContext? _db;

    public ProductionEventWriter(
        IProductionTruthStore truthStore,
        NovelAgentDbContext? db = null)
    {
        _truthStore = truthStore;
        _db = db;
    }

    public async Task<ProductionEvent> AppendChapterStageAsync(
        AppendChapterProductionEventRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new InvalidOperationException("Production event requires runtimeRunId, userId and projectId.");
        }

        var chapterId = await ResolveCanonicalChapterIdAsync(request.ProjectId, request.ChapterId, cancellationToken)
            .ConfigureAwait(false);
        return await _truthStore.AppendEventAsync(
                new CreateProductionEventRequest(
                    RuntimeRunId: request.RuntimeRunId,
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    ChapterId: chapterId,
                    PackageId: request.PackageId,
                    EventType: request.EventType,
                    Stage: request.Stage,
                    Status: request.Status,
                    Message: request.Message,
                    ArtifactType: request.ArtifactType,
                    ArtifactId: request.ArtifactId,
                    DataJson: request.Data == null ? null : JsonSerializer.Serialize(request.Data, EventJsonOptions)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ChapterDraft> AppendChapterDraftAsync(
        AppendChapterDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var chapterId = await ResolveCanonicalChapterIdAsync(request.ProjectId, request.ChapterId, cancellationToken)
            .ConfigureAwait(false) ?? request.ChapterId;
        return await _truthStore.CreateChapterDraftAsync(
                new CreateChapterDraftRequest(
                    request.UserId,
                    request.ProjectId,
                    request.RuntimeRunId,
                    chapterId,
                    request.PackageId,
                    request.ArtifactId,
                    request.Status,
                    request.DraftContent,
                    request.ChangesJson,
                    request.RepairAttemptCount,
                    request.HasChanges),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ChapterChange> AppendChapterChangeAsync(
        AppendChapterChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var chapterId = await ResolveCanonicalChapterIdAsync(request.ProjectId, request.ChapterId, cancellationToken)
            .ConfigureAwait(false) ?? request.ChapterId;
        return await _truthStore.CreateChapterChangeAsync(
                new CreateChapterChangeRequest(
                    request.UserId,
                    request.ProjectId,
                    request.RuntimeRunId,
                    chapterId,
                    request.PackageId,
                    request.ChangesJson,
                    request.CanonicalChangesJson,
                    request.ParseStatus,
                    request.ParseError,
                    request.AppliedToFactSnapshot),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GenerationGateReportRecord> AppendGenerationGateReportAsync(
        AppendGenerationGateReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var chapterId = await ResolveCanonicalChapterIdAsync(request.ProjectId, request.ChapterId, cancellationToken)
            .ConfigureAwait(false) ?? request.ChapterId;
        return await _truthStore.CreateGenerationGateReportAsync(
                new CreateGenerationGateReportRequest(
                    request.UserId,
                    request.ProjectId,
                    request.RuntimeRunId,
                    chapterId,
                    request.PackageId,
                    request.ArtifactId,
                    request.Status,
                    request.ReportJson,
                    request.ProtocolPassed,
                    request.ChangesDetected,
                    request.FactSnapshotPassed,
                    request.BlueprintPassed,
                    request.RagPassed,
                    request.IssueCount,
                    request.RepairHintCount,
                    request.ValidatedAt),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentReviewRecord> AppendAgentReviewAsync(
        AppendAgentReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var chapterId = await ResolveCanonicalChapterIdAsync(request.ProjectId, request.ChapterId, cancellationToken)
            .ConfigureAwait(false) ?? request.ChapterId;
        return await _truthStore.CreateAgentReviewAsync(
                new CreateAgentReviewRequest(
                    request.UserId,
                    request.ProjectId,
                    request.RuntimeRunId,
                    chapterId,
                    request.PackageId,
                    request.ReviewId,
                    request.OverallResult,
                    request.ValidationOverallResult,
                    request.RequiresRewrite,
                    request.QualityScore,
                    request.ContentLength,
                    request.CheckCount,
                    request.Summary,
                    request.ReviewJson,
                    request.ReviewedAt,
                    request.MeetsAcceptedCreativeIntents,
                    request.ContinuityRisk,
                    request.ChapterPacing,
                    request.RecommendedAction),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string?> ResolveCanonicalChapterIdAsync(
        string projectId,
        string? chapterId,
        CancellationToken cancellationToken)
    {
        var value = chapterId?.Trim();
        if (_db == null || string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(value))
            return value;

        return await ChapterIdentityResolver.ResolveCanonicalChapterIdAsync(
                _db,
                projectId,
                value,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

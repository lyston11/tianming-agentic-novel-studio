using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ChapterFactSnapshotUpdater : IChapterFactSnapshotUpdater
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NovelAgentDbContext _db;
    private readonly IProductionTruthStore _truthStore;
    private readonly IProductionEventWriter _eventWriter;

    public ChapterFactSnapshotUpdater(
        NovelAgentDbContext db,
        IProductionTruthStore truthStore,
        IProductionEventWriter eventWriter)
    {
        _db = db;
        _truthStore = truthStore;
        _eventWriter = eventWriter;
    }

    public async Task UpdateAsync(
        string userId,
        string projectId,
        string chapterId,
        string runtimeRunId,
        string? packageId,
        ChapterContinuityFacts facts,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(chapterId))
        {
            throw new InvalidOperationException("Chapter fact snapshot update requires userId, projectId and chapterId.");
        }

        var version = await ResolveLatestVersionAsync(userId, projectId, chapterId, ct)
            .ConfigureAwait(false);
        if (version == null)
            throw new InvalidOperationException($"Chapter version not found for fact extraction snapshot: {projectId}/{chapterId}.");

        var resolvedChapterId = version.ChapterId;
        var snapshot = await _truthStore.SaveFactSnapshotAsync(
                new SaveProjectFactSnapshotRequest(
                    userId,
                    projectId,
                    resolvedChapterId,
                    version.Id,
                    JsonSerializer.Serialize(new
                    {
                        chapterId = resolvedChapterId,
                        chapterVersionId = version.Id,
                        versionNumber = version.VersionNumber,
                        packageId = packageId ?? version.PackageId ?? string.Empty,
                        source = "chapter_fact_extraction",
                        facts.ChapterTitle,
                        facts.ProtagonistName,
                        facts.ProtagonistIdentity,
                        facts.ProtagonistStatus,
                        facts.CurrentLocation,
                        facts.SystemState,
                        facts.EquipmentState,
                        facts.KeyEvents,
                        facts.EndingState,
                        chapterEndingState = facts.EndingState,
                        facts.NextChapterMustCarry,
                        facts.ExtractedAt,
                        facts.SourceRunId
                    }, SnapshotJsonOptions),
                    "chapter_fact_extraction"),
                ct)
            .ConfigureAwait(false);
        await _truthStore.MarkChapterChangesAppliedToFactSnapshotAsync(
                new MarkChapterChangesAppliedToFactSnapshotRequest(
                    userId,
                    projectId,
                    runtimeRunId,
                    resolvedChapterId,
                    packageId ?? version.PackageId),
                ct)
            .ConfigureAwait(false);

        await _eventWriter.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: runtimeRunId,
                    UserId: userId,
                    ProjectId: projectId,
                    ChapterId: resolvedChapterId,
                    PackageId: packageId ?? version.PackageId,
                    EventType: "chapter_continuity_facts_extracted",
                    Stage: NovelAgentProductionStages.FactsPersisted,
                    Status: "completed",
                    Message: "章节连续性事实已由 LLM 沉淀并追加生产事实快照。",
                    ArtifactType: "project_fact_snapshot",
                    ArtifactId: snapshot.Id,
                    Data: new
                    {
                        factSnapshotId = snapshot.Id,
                        snapshot.VersionNumber,
                        protagonistName = facts.ProtagonistName,
                        endingState = facts.EndingState,
                        nextChapterMustCarry = facts.NextChapterMustCarry
                    }),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<ChapterVersion?> ResolveLatestVersionAsync(
        string userId,
        string projectId,
        string chapterId,
        CancellationToken ct)
    {
        var version = await QueryLatestVersion(userId, projectId, chapterId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (version != null)
            return version;

        var chapterNumber = ChapterParserHelper.ParseChapterId(chapterId)?.chapterNumber;
        var chapter = await _db.Chapters
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .Where(c =>
                c.Id == chapterId ||
                c.Title == chapterId ||
                (chapterNumber.HasValue && c.ChapterNumber == chapterNumber.Value))
            .OrderBy(c => c.ChapterNumber)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return chapter == null
            ? null
            : await QueryLatestVersion(userId, projectId, chapter.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
    }

    private IQueryable<ChapterVersion> QueryLatestVersion(
        string userId,
        string projectId,
        string chapterId)
    {
        return _db.ChapterVersions
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.ProjectId == projectId && v.ChapterId == chapterId)
            .OrderByDescending(v => v.VersionNumber)
            .ThenByDescending(v => v.CreatedAt);
    }
}

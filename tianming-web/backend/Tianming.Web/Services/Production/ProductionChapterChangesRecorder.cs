using System.Text.Encodings.Web;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionChapterChangesRecorder : IChapterChangesRecorder
{
    private static readonly JsonSerializerOptions ChangesJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IProductionEventWriter? _writer;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly string _userId;
    private readonly string _projectId;

    public ProductionChapterChangesRecorder(
        IProductionEventWriter writer,
        string userId,
        string projectId)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _userId = userId;
        _projectId = projectId;
    }

    public ProductionChapterChangesRecorder(
        IServiceScopeFactory scopeFactory,
        string userId,
        string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _userId = userId;
        _projectId = projectId;
    }

    public async Task RecordAsync(
        NovelAgentRun run,
        string chapterId,
        ChapterChanges changes,
        string? changesJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(run.RunId) ||
            string.IsNullOrWhiteSpace(_userId) ||
            string.IsNullOrWhiteSpace(_projectId) ||
            string.IsNullOrWhiteSpace(chapterId))
        {
            throw new InvalidOperationException("Chapter changes production event requires runId, userId, projectId and chapterId.");
        }

        if (_writer != null)
        {
            await AppendAsync(_writer, run, chapterId, changes, changesJson, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (_scopeFactory == null)
            throw new InvalidOperationException("Chapter changes production event requires IProductionEventWriter or IServiceScopeFactory.");

        using var scope = _scopeFactory.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IProductionEventWriter>();
        await AppendAsync(writer, run, chapterId, changes, changesJson, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AppendAsync(
        IProductionEventWriter writer,
        NovelAgentRun run,
        string chapterId,
        ChapterChanges changes,
        string? changesJson,
        CancellationToken cancellationToken)
    {
        await writer.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: run.RunId,
                    UserId: _userId,
                    ProjectId: _projectId,
                    ChapterId: chapterId,
                    PackageId: run.ContextPackage?.PackageId,
                    EventType: "chapter_changes_recorded",
                    Stage: NovelAgentProductionStages.ChangesExtracted,
                    Status: "completed",
                    Message: "章节 CHANGES 已写入生产事实事件。",
                    ArtifactType: "chapter_changes",
                    ArtifactId: $"{chapterId}:changes",
                    Data: new
                    {
                        chapterId,
                        changesJson,
                        characterStateChangeCount = changes.CharacterStateChanges.Count,
                        conflictProgressCount = changes.ConflictProgress.Count,
                        newPlotPointCount = changes.NewPlotPoints.Count,
                        foreshadowingActionCount = changes.ForeshadowingActions.Count,
                        locationStateChangeCount = changes.LocationStateChanges.Count,
                        factionStateChangeCount = changes.FactionStateChanges.Count,
                        characterMovementCount = changes.CharacterMovements.Count,
                        itemTransferCount = changes.ItemTransfers.Count,
                        secretRevealChangeCount = changes.SecretRevealChanges.Count,
                        pledgeConstraintChangeCount = changes.PledgeConstraintChanges.Count,
                        deadlineConstraintChangeCount = changes.DeadlineConstraintChanges.Count,
                        changes
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        var parsed = BuildChapterChangeParseResult(changes, changesJson);
        await writer.AppendChapterChangeAsync(
                new AppendChapterChangeRequest(
                    UserId: _userId,
                    ProjectId: _projectId,
                    RuntimeRunId: run.RunId,
                    ChapterId: chapterId,
                    PackageId: run.ContextPackage?.PackageId,
                    ChangesJson: parsed.RawJson,
                    CanonicalChangesJson: parsed.CanonicalJson,
                    ParseStatus: parsed.Status,
                    ParseError: parsed.Error,
                    AppliedToFactSnapshot: false),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ChapterChangeParseResult BuildChapterChangeParseResult(
        ChapterChanges changes,
        string? changesJson)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
        {
            var serialized = JsonSerializer.Serialize(changes, ChangesJsonOptions);
            return new ChapterChangeParseResult(serialized, serialized, "parsed", null);
        }

        var raw = changesJson.Trim();
        if (ChapterChangesText.TryDeserializeChanges(raw, out var parsedChanges))
        {
            var canonical = JsonSerializer.Serialize(parsedChanges, ChangesJsonOptions);
            return new ChapterChangeParseResult(raw, canonical, "parsed", null);
        }

        var fallback = JsonSerializer.Serialize(changes, ChangesJsonOptions);
        return new ChapterChangeParseResult(raw, fallback, "parse_failed", "CHANGES JSON 不是可解析对象。");
    }

    private sealed record ChapterChangeParseResult(
        string RawJson,
        string CanonicalJson,
        string Status,
        string? Error);
}

using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ChapterFactOutboxProcessor : IChapterFactOutboxProcessor
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IChapterFactWriter _writer;
    private readonly IWritingModelCompletionService _completion;
    private readonly IChapterContinuityFactPersister _persister;
    private readonly IChapterFactSnapshotUpdater _snapshotUpdater;
    private readonly ILogger<ChapterFactOutboxProcessor> _logger;

    public ChapterFactOutboxProcessor(
        IChapterFactWriter writer,
        IWritingModelCompletionService completion,
        IChapterContinuityFactPersister persister,
        IChapterFactSnapshotUpdater snapshotUpdater,
        ILogger<ChapterFactOutboxProcessor> logger)
    {
        _writer = writer;
        _completion = completion;
        _persister = persister;
        _snapshotUpdater = snapshotUpdater;
        _logger = logger;
    }

    public async Task ProcessAsync(OutboxEvent evt, CancellationToken ct = default)
    {
        if (evt.EventType != "extract_chapter_continuity_facts" ||
            evt.AggregateType != "chapter")
        {
            throw new InvalidOperationException($"Unsupported chapter fact outbox event {evt.EventType}/{evt.AggregateType}.");
        }

        if (string.IsNullOrWhiteSpace(evt.ProjectId))
            throw new InvalidOperationException("Chapter fact extraction outbox requires project id.");

        var payload = JsonSerializer.Deserialize<ChapterFactOutboxPayload>(evt.PayloadJson, PayloadJsonOptions)
            ?? throw new InvalidOperationException("Chapter fact extraction outbox payload is empty.");
        if (string.IsNullOrWhiteSpace(payload.CommittedContent))
            throw new InvalidOperationException("Chapter fact extraction outbox payload has no committed content.");

        var run = payload.Run ?? new NovelAgentRun
        {
            RunId = evt.RuntimeRunId ?? string.Empty,
            TargetChapterId = evt.AggregateId
        };
        if (string.IsNullOrWhiteSpace(run.RunId))
            run.RunId = evt.RuntimeRunId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(run.TargetChapterId))
            run.TargetChapterId = evt.AggregateId;

        var contextPackage = payload.ContextPackage ?? new ChapterContextPackageSummary
        {
            ChapterId = evt.AggregateId
        };
        if (string.IsNullOrWhiteSpace(contextPackage.ChapterId))
            contextPackage.ChapterId = evt.AggregateId;

        var result = await _writer.ExtractAndPersistAsync(
                new ChapterFactWriteRequest
                {
                    Run = run,
                    ContextPackage = contextPackage,
                    CommittedContent = payload.CommittedContent,
                    CompleteAsync = (system, user, cancellationToken) =>
                        _completion.CompleteAsync(evt.UserId, system, user, cancellationToken),
                    PersistAsync = (facts, cancellationToken) =>
                        _persister.PersistAsync(evt.UserId, evt.ProjectId!, facts, cancellationToken)
                },
                ct)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.Message)
                    ? "Chapter continuity fact extraction failed."
                    : result.Message);
        }

        if (result.Facts != null)
        {
            await _snapshotUpdater.UpdateAsync(
                    evt.UserId,
                    evt.ProjectId!,
                    run.TargetChapterId,
                    run.RunId,
                    contextPackage.PackageId,
                    result.Facts,
                    ct)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Extracted chapter continuity facts for run {RunId}, chapter {ChapterId}",
            run.RunId,
            run.TargetChapterId);
    }

    private sealed class ChapterFactOutboxPayload
    {
        public NovelAgentRun? Run { get; set; }
        public ChapterContextPackageSummary? ContextPackage { get; set; }
        public string CommittedContent { get; set; } = string.Empty;
    }
}

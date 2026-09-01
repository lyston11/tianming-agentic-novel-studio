using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Creative;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ChapterCommitPostCommitFinalizer : IChapterCommitPostCommitFinalizer
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IChapterCommitTruthRecorder _truthRecorder;
    private readonly ICreativeIntentService _creativeIntents;
    private readonly IProductionEventWriter _events;

    public ChapterCommitPostCommitFinalizer(
        IChapterCommitTruthRecorder truthRecorder,
        ICreativeIntentService creativeIntents,
        IProductionEventWriter events)
    {
        _truthRecorder = truthRecorder;
        _creativeIntents = creativeIntents;
        _events = events;
    }

    public async Task ProcessOutboxAsync(
        OutboxEvent evt,
        CancellationToken cancellationToken = default)
    {
        if (evt.EventType != "finalize_chapter_commit_metadata" ||
            evt.AggregateType != "chapter")
        {
            throw new InvalidOperationException($"Unsupported chapter commit post-commit outbox event {evt.EventType}/{evt.AggregateType}.");
        }

        var payload = JsonSerializer.Deserialize<ChapterCommitPostCommitPayload>(evt.PayloadJson, PayloadJsonOptions)
            ?? throw new InvalidOperationException("Chapter commit post-commit outbox payload is empty.");
        payload.RuntimeRunId = FirstNonEmpty(payload.RuntimeRunId, evt.RuntimeRunId);
        payload.UserId = FirstNonEmpty(payload.UserId, evt.UserId);
        payload.ProjectId = FirstNonEmpty(payload.ProjectId, evt.ProjectId);
        payload.TargetChapterId = FirstNonEmpty(payload.TargetChapterId, evt.AggregateId);

        await FinalizeAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task FinalizeAsync(
        ChapterCommitPostCommitPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(payload.UserId) ||
            string.IsNullOrWhiteSpace(payload.ProjectId))
        {
            throw new InvalidOperationException("Chapter commit post-commit finalization requires runtimeRunId, userId and projectId.");
        }

        var record = await _truthRecorder.RecordAsync(
                new RecordChapterCommitTruthRequest(
                    RuntimeRunId: payload.RuntimeRunId,
                    UserId: payload.UserId,
                    ProjectId: payload.ProjectId,
                    TargetChapterId: payload.TargetChapterId,
                    Message: payload.Message,
                    ContextPackage: payload.ContextPackage,
                    DraftArtifact: payload.DraftArtifact,
                    GateReport: payload.GateReport,
                    PostGenerationReview: payload.PostGenerationReview,
                    ContinuityFacts: payload.ContinuityFacts),
                cancellationToken)
            .ConfigureAwait(false);

        await MarkKnowledgeBindingsUsedAsync(payload, record.Chapter.Id, cancellationToken).ConfigureAwait(false);
        await MarkCreativeIntentsExecutedAsync(payload, record.Chapter.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkCreativeIntentsExecutedAsync(
        ChapterCommitPostCommitPayload payload,
        string committedChapterId,
        CancellationToken cancellationToken)
    {
        var package = payload.ContextPackage;
        if (package == null || package.AcceptedCreativeIntents.Count == 0)
            return;

        var execution = await _creativeIntents.MarkPackageIntentsExecutedAsync(
                payload.UserId,
                payload.ProjectId,
                package,
                $"章节 {package.ChapterId} 已提交书城，生产包内创意已执行。",
                cancellationToken)
            .ConfigureAwait(false);
        if (execution.ExecutedCount <= 0)
            return;

        await _events.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: payload.RuntimeRunId,
                    UserId: payload.UserId,
                    ProjectId: payload.ProjectId,
                    ChapterId: committedChapterId,
                    PackageId: package.PackageId,
                    EventType: "creative_intents_executed",
                    Stage: NovelAgentProductionStages.FactsPersisted,
                    Status: "completed",
                    Message: $"已将 {execution.ExecutedCount} 条生产包创意标记为已执行。",
                    ArtifactType: "creative_intents",
                    ArtifactId: committedChapterId,
                    Data: new
                    {
                        packageId = package.PackageId,
                        chapterId = committedChapterId,
                        packageChapterId = package.ChapterId,
                        execution.ExecutedCount,
                        intentIds = execution.IntentIds
                    }),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task MarkKnowledgeBindingsUsedAsync(
        ChapterCommitPostCommitPayload payload,
        string committedChapterId,
        CancellationToken cancellationToken)
    {
        var package = payload.ContextPackage;
        if (package == null || package.KnowledgeBindings.Count == 0)
            return;

        var bindings = package.KnowledgeBindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.KnowledgeId) ||
                              !string.IsNullOrWhiteSpace(binding.Title))
            .Select(binding => new
            {
                knowledgeId = binding.KnowledgeId,
                title = binding.Title,
                entryType = binding.EntryType,
                projectUsageStatus = "used",
                weight = binding.Weight
            })
            .Take(24)
            .ToList();
        if (bindings.Count == 0)
            return;

        await _events.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: payload.RuntimeRunId,
                    UserId: payload.UserId,
                    ProjectId: payload.ProjectId,
                    ChapterId: committedChapterId,
                    PackageId: package.PackageId,
                    EventType: "knowledge_bindings_used",
                    Stage: NovelAgentProductionStages.FactsPersisted,
                    Status: "completed",
                    Message: $"本章生产包实际使用 {bindings.Count} 条项目知识绑定。",
                    ArtifactType: "knowledge_bindings",
                    ArtifactId: committedChapterId,
                    Data: new
                    {
                        packageId = package.PackageId,
                        chapterId = committedChapterId,
                        packageChapterId = package.ChapterId,
                        bindingCount = bindings.Count,
                        knowledgeIds = bindings.Select(binding => binding.knowledgeId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
                        bindings
                    }),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

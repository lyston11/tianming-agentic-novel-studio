using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IChapterCommitPostCommitFinalizer
{
    Task FinalizeAsync(
        ChapterCommitPostCommitPayload payload,
        CancellationToken cancellationToken = default);

    Task ProcessOutboxAsync(
        OutboxEvent evt,
        CancellationToken cancellationToken = default);
}

public sealed class ChapterCommitPostCommitPayload
{
    public string RuntimeRunId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string TargetChapterId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ChapterContextPackageSummary? ContextPackage { get; set; }
    public ChapterDraftArtifact? DraftArtifact { get; set; }
    public GenerationGateReport? GateReport { get; set; }
    public NovelAgentPostGenerationReview? PostGenerationReview { get; set; }
    public ChapterContinuityFacts? ContinuityFacts { get; set; }
}

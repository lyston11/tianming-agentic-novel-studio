using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IChapterCommitTruthRecorder
{
    Task<ChapterCommitTruthRecord> RecordAsync(
        RecordChapterCommitTruthRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record RecordChapterCommitTruthRequest(
    string RuntimeRunId,
    string UserId,
    string ProjectId,
    string? TargetChapterId,
    string Message,
    ChapterContextPackageSummary? ContextPackage,
    ChapterDraftArtifact? DraftArtifact,
    GenerationGateReport? GateReport,
    NovelAgentPostGenerationReview? PostGenerationReview,
    ChapterContinuityFacts? ContinuityFacts = null);

public sealed record ChapterCommitTruthRecord(
    Chapter Chapter,
    ChapterVersion Version,
    ProjectFactSnapshot FactSnapshot,
    ProductionEvent Event);

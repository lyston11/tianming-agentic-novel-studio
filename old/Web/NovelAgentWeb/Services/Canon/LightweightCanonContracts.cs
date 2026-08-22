using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Canon;

public sealed record CanonEvidenceSpan(int Start, int End, string Quote);

public sealed record LightweightSummaryItemDraft(
    string Id,
    string Category,
    string Content,
    CanonEvidenceSpan Evidence);

public sealed record CanonChangeDraft(
    string Id,
    string ChangeType,
    string Subject,
    string Description,
    CanonEvidenceSpan Evidence);

public sealed record LightweightCanonDraft(
    IReadOnlyList<LightweightSummaryItemDraft> SummaryItems,
    IReadOnlyList<CanonChangeDraft> CanonChanges);

public sealed record LightweightCanonExtractionRequest(
    string UserId,
    string ProjectId,
    string GoalId,
    string BranchId,
    string CandidateChapterId,
    int CandidateVersion,
    string ChapterId,
    string ChapterBody);

public sealed record LightweightCanonSemanticReviewRequest(
    LightweightCanonExtractionRequest Source,
    LightweightCanonDraft Draft);

public sealed record LightweightCanonSemanticReview(
    IReadOnlyList<string> ApprovedSummaryItemIds,
    IReadOnlyList<string> ApprovedCanonChangeIds,
    IReadOnlyDictionary<string, string> RejectionReasons);

public sealed record LightweightCanonExtractionResult(
    ContinuitySummary Summary,
    IReadOnlyList<CanonChange> CanonChanges);

public sealed record LightweightCanonArtifact(
    string SourcePriority,
    string BodyArtifactId,
    IReadOnlyList<LightweightSummaryItemDraft> SummaryItems,
    IReadOnlyList<CanonChangeDraft> CanonChanges,
    IReadOnlyDictionary<string, string> RejectionReasons);

public interface ILightweightCanonExtractionModelClient
{
    Task<LightweightCanonDraft> ExtractAsync(
        LightweightCanonExtractionRequest request,
        CancellationToken cancellationToken = default);
}

public interface ILightweightCanonSemanticReviewModelClient
{
    Task<LightweightCanonSemanticReview> ReviewAsync(
        LightweightCanonSemanticReviewRequest request,
        CancellationToken cancellationToken = default);
}

public interface IContinuitySummaryExtractor
{
    Task<LightweightCanonExtractionResult> ExtractAsync(
        string candidateChapterId,
        CancellationToken cancellationToken = default);
}

public static class CanonEvidenceValidator
{
    public static void Validate(
        CanonEvidenceSpan evidence,
        string body,
        string proposalType,
        string proposalId)
    {
        if (evidence.Start < 0 || evidence.End <= evidence.Start || evidence.End > body.Length)
            throw new InvalidOperationException($"{proposalType} {proposalId} 的正文证据区间无效。");
        if (!string.Equals(body[evidence.Start..evidence.End], evidence.Quote, StringComparison.Ordinal))
            throw new InvalidOperationException($"{proposalType} {proposalId} 的证据文本与章节正文不一致。");
    }
}

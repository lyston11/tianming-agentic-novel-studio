namespace TM.Web.NovelAgentWeb.Services.Quality;

public enum ReviewVerdict
{
    Pass,
    PassWithSuggestions,
    ReworkRequired,
    NeedsDecision
}

public sealed record PotentialViolation(
    string Category,
    string Claim,
    string EvidenceRef,
    string Impact);

public sealed record ContinuityReviewRequest(
    string UserId,
    string ProjectId,
    string GoalId,
    string ChapterId,
    string DraftContent,
    IReadOnlyList<string> EvidenceJson,
    IReadOnlyList<PotentialViolation> PotentialViolations,
    string QualityContractVersion);

public sealed record ContinuityClaimDecision(
    string Claim,
    IReadOnlyList<string> SupportingEvidence,
    IReadOnlyList<string> ContradictingEvidence,
    string NarrativeExplanation,
    double Confidence,
    string Verdict);

public sealed record ContinuityReviewDecision(
    ReviewVerdict Verdict,
    IReadOnlyList<ContinuityClaimDecision> Claims,
    IReadOnlyList<string> Recommendations);

public sealed record ContinuityReviewArtifact(
    ReviewVerdict Verdict,
    IReadOnlyList<PotentialViolation> PotentialViolations,
    IReadOnlyList<ContinuityClaimDecision> Claims,
    IReadOnlyList<string> Recommendations);

public sealed record LiteraryReviewRequest(
    string UserId,
    string ProjectId,
    string GoalId,
    string ChapterId,
    string DraftContent,
    string QualityContractVersion);

public sealed record LiteraryDimensionReview(
    string Assessment,
    IReadOnlyList<string> Evidence,
    string Suggestion);

public sealed record LiteraryReviewDecision(
    ReviewVerdict Verdict,
    IReadOnlyDictionary<string, LiteraryDimensionReview> Dimensions,
    IReadOnlyList<string> Suggestions);

public interface IPotentialViolationDetector
{
    IReadOnlyList<PotentialViolation> Detect(ContinuityReviewRequest request);
}

public interface IContinuityReviewModelClient
{
    Task<ContinuityReviewDecision> ReviewAsync(
        ContinuityReviewRequest request,
        CancellationToken cancellationToken = default);
}

public interface ILiteraryReviewModelClient
{
    Task<LiteraryReviewDecision> ReviewAsync(
        LiteraryReviewRequest request,
        CancellationToken cancellationToken = default);
}

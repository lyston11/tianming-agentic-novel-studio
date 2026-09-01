namespace TM.Web.NovelAgentWeb.Services.Quality;

public sealed class PotentialViolationDetector : IPotentialViolationDetector
{
    public IReadOnlyList<PotentialViolation> Detect(ContinuityReviewRequest request)
    {
        var violations = new List<PotentialViolation>();
        if (string.IsNullOrWhiteSpace(request.DraftContent))
        {
            violations.Add(new PotentialViolation(
                "protocol",
                "候选正文为空",
                "draft",
                "high"));
        }
        if (request.EvidenceJson.Count == 0)
        {
            violations.Add(new PotentialViolation(
                "evidence",
                "没有提供可用于连续性裁决的正文证据",
                "evidence_bundle",
                "medium"));
        }
        return violations;
    }
}

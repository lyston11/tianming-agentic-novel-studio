namespace TM.Web.NovelAgentWeb.Data.Entities;

public class RevisionPlan
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string? CreativeIntentId { get; set; }
    public string? KnowledgeConflictReportId { get; set; }
    public string? SessionId { get; set; }
    public string? RuntimeRunId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string Source { get; set; } = "creative_intent";
    public string PlanType { get; set; } = "future_carry";
    public string TargetScope { get; set; } = "project";
    public string? TargetVolumeId { get; set; }
    public string? TargetChapterId { get; set; }
    public string? TargetChapterLogicalId { get; set; }
    public string? TargetChapterDisplayName { get; set; }
    public string Status { get; set; } = "draft";
    public string RequirementsJson { get; set; } = "[]";
    public string ContinuityRequirementsJson { get; set; } = "[]";
    public string ImpactAnalysisJson { get; set; } = "{}";
    public string AffectedChapterIdsJson { get; set; } = "[]";
    public string InvalidatedPackageIdsJson { get; set; } = "[]";
    public string RiskLevel { get; set; } = "medium";
    public string Recommendation { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
    public CreativeIntent? CreativeIntent { get; set; }
    public KnowledgeConflictReport? KnowledgeConflictReport { get; set; }
}

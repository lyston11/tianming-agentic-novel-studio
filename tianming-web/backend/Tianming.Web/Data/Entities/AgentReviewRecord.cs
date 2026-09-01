namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentReviewRecord
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string RuntimeRunId { get; set; } = null!;
    public string ChapterId { get; set; } = null!;
    public string? PackageId { get; set; }
    public string ReviewId { get; set; } = null!;
    public string OverallResult { get; set; } = "Unknown";
    public string ValidationOverallResult { get; set; } = string.Empty;
    public bool RequiresRewrite { get; set; }
    public int QualityScore { get; set; }
    public int ContentLength { get; set; }
    public int CheckCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public bool MeetsAcceptedCreativeIntents { get; set; } = true;
    public string ContinuityRisk { get; set; } = string.Empty;
    public string ChapterPacing { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string ReviewJson { get; set; } = "{}";
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public NovelProject Project { get; set; } = null!;
    public Chapter? Chapter { get; set; }
}

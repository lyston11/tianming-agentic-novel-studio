namespace TM.Web.NovelAgentWeb.Models.Chapters;

/// <summary>
/// Read-only comparison between two committed chapter versions.
/// </summary>
public class ChapterVersionCompareResponse
{
    public required string ChapterId { get; set; }
    public required ChapterVersionResponse Left { get; set; }
    public required ChapterVersionResponse Right { get; set; }
    public int WordCountDelta { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<ChapterVersionDiffBlock> DiffBlocks { get; set; } = new();
    public ChapterVersionProductionAlignment ProductionAlignment { get; set; } = new();
}

/// <summary>
/// Paragraph-level diff block for chapter version comparison.
/// </summary>
public class ChapterVersionDiffBlock
{
    public required string Kind { get; set; }
    public string LeftText { get; set; } = string.Empty;
    public string RightText { get; set; } = string.Empty;
}

public class ChapterVersionProductionAlignment
{
    public string LeftPackageId { get; set; } = string.Empty;
    public string RightPackageId { get; set; } = string.Empty;
    public string AgentReviewDecision { get; set; } = string.Empty;
    public List<string> RebuiltFromPackageIds { get; set; } = new();
    public List<ChapterVersionCreativeIntentAlignment> AcceptedCreativeIntents { get; set; } = new();
    public List<ChapterVersionRevisionPlanAlignment> SourceRevisionPlans { get; set; } = new();
    public List<ChapterVersionAgentReviewCheckAlignment> AgentReviewChecks { get; set; } = new();
}

public class ChapterVersionCreativeIntentAlignment
{
    public string IntentId { get; set; } = string.Empty;
    public string NormalizedIntent { get; set; } = string.Empty;
    public string TargetScope { get; set; } = string.Empty;
    public string TargetChapterId { get; set; } = string.Empty;
    public string ImpactLevel { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = "accepted";
}

public class ChapterVersionRevisionPlanAlignment
{
    public string RevisionPlanId { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public string TargetScope { get; set; } = string.Empty;
    public string TargetChapterId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> AffectedChapterIds { get; set; } = new();
    public List<string> InvalidatedPackageIds { get; set; } = new();
    public string RiskLevel { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public class ChapterVersionAgentReviewCheckAlignment
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> Evidence { get; set; } = new();
}

namespace TM.Web.NovelAgentWeb.Models.Chapters;

/// <summary>
/// Response model for a committed chapter version and its production lineage.
/// </summary>
public class ChapterVersionResponse
{
    public required string Id { get; set; }
    public required string ChapterId { get; set; }
    public required string ContentDocumentId { get; set; }
    public required int VersionNumber { get; set; }
    public required string Title { get; set; }
    public required int WordCount { get; set; }
    public required string Status { get; set; }
    public string? RuntimeRunId { get; set; }
    public string? PackageId { get; set; }
    public string? KernelVersion { get; set; }
    public string? PromptVersion { get; set; }
    public string? GateReportJson { get; set; }
    public string? AgentReviewJson { get; set; }
    public List<string> RebuiltFromPackageIds { get; set; } = new();
    public bool IsCurrent { get; set; }
    public string ContentPreview { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ChapterVersion
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string ChapterId { get; set; } = null!;
    public string ContentDocumentId { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string Title { get; set; } = null!;
    public int WordCount { get; set; }
    public string Status { get; set; } = "draft";
    public string? RuntimeRunId { get; set; }
    public string? PackageId { get; set; }
    public string? GateReportJson { get; set; }
    public string? AgentReviewJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public NovelProject Project { get; set; } = null!;
    public Chapter Chapter { get; set; } = null!;
    public ContentDocument ContentDocument { get; set; } = null!;
}

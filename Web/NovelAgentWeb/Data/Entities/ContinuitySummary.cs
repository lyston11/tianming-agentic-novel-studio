namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class ContinuitySummary
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string ChapterVersionId { get; set; } = string.Empty;
    public string? BranchId { get; set; }
    public int Version { get; set; } = 1;
    public string SummaryJson { get; set; } = "{}";
    public string EvidenceRefsJson { get; set; } = "[]";
    public string Status { get; set; } = "candidate";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

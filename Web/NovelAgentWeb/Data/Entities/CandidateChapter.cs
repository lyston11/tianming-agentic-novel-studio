namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class CandidateChapter
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public int ChapterNumber { get; set; }
    public int Version { get; set; } = 1;
    public string CurrentArtifactId { get; set; } = string.Empty;
    public string? DependsOnCandidateChapterId { get; set; }
    public string Status { get; set; } = "candidate";
    public string Authorship { get; set; } = "agent";
    public bool IsProtected { get; set; }
    public string? ContinuitySummaryId { get; set; }
    public string ReviewArtifactIdsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

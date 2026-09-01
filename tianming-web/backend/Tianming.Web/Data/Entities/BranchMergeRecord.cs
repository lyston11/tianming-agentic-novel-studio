namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class BranchMergeRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public int StartChapterNumber { get; set; }
    public int EndChapterNumber { get; set; }
    public string CandidateVersionsJson { get; set; } = "{}";
    public string PreviousCanonVersion { get; set; } = string.Empty;
    public string NewCanonVersion { get; set; } = string.Empty;
    public string MergedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

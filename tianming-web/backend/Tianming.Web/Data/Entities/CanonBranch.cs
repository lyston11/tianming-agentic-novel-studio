namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class CanonBranch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string CanonBaselineVersion { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public int StartChapterNumber { get; set; }
    public int EndChapterNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? MergedAt { get; set; }
}

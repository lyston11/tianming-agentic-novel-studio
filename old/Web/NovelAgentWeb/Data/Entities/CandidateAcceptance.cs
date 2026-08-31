namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class CandidateAcceptance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public string CandidateChapterId { get; set; } = string.Empty;
    public int CandidateVersion { get; set; }
    public string Decision { get; set; } = "accepted";
    public string DecidedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

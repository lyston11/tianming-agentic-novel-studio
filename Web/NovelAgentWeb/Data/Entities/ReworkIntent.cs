namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class ReworkIntent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public string CandidateChapterId { get; set; } = string.Empty;
    public int CandidateVersion { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string TargetScope { get; set; } = "chapter";
    public int? SelectionStart { get; set; }
    public int? SelectionEnd { get; set; }
    public string SelectedText { get; set; } = string.Empty;
    public string UserDescription { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty;
    public string DesiredEffect { get; set; } = string.Empty;
    public string PreserveJson { get; set; } = "[]";
    public string MayChangeJson { get; set; } = "[]";
    public string MustNotChangeJson { get; set; } = "[]";
    public string AcceptanceCriteriaJson { get; set; } = "[]";
    public string ImpactLevel { get; set; } = "copy";
    public string ImpactAssessmentJson { get; set; } = "{}";
    public int AttemptCount { get; set; }
    public string Status { get; set; } = "proposed";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class GoalRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ConstraintChangesJson { get; set; } = "{}";
    public string ReusableArtifactIdsJson { get; set; } = "[]";
    public string InvalidatedArtifactIdsJson { get; set; } = "[]";
    public string AffectedNodeIdsJson { get; set; } = "[]";
    public string? TaskGraphVersionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

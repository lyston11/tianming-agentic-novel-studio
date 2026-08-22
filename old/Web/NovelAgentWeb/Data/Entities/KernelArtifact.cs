namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class KernelArtifact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string? BranchId { get; set; }
    public string ArtifactType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string ContentJson { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public string Status { get; set; } = "proposed";
    public string Authorship { get; set; } = "agent";
    public bool IsProtected { get; set; }
    public string? ModelExecutionId { get; set; }
    public string? CausationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

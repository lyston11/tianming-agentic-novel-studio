namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class ProjectCollaborationDecision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string MemoryKind { get; set; } = string.Empty;
    public string ContentJson { get; set; } = "{}";
    public string Scope { get; set; } = "project";
    public string? EffectiveGoalId { get; set; }
    public bool ExpiresAfterGoal { get; set; }
    public string Source { get; set; } = "user";
    public string? SourceSessionStateId { get; set; }
    public string? SourceSuggestionId { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

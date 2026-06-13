namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ProjectKnowledgeUsage
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string KnowledgeId { get; set; } = null!;
    public string Status { get; set; } = "imported";
    public string? SourceSessionId { get; set; }
    public string? SourceRunId { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public int UsageCount { get; set; }
    public string? Note { get; set; }
    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
}

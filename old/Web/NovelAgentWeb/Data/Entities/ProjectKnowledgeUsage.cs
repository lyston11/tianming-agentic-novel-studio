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
    public string Role { get; set; } = "Reference";
    public string Scope { get; set; } = "ProjectWide";
    public int Priority { get; set; } = 50;
    public string ConstraintLevel { get; set; } = "Reference";
    public string PackagePolicy { get; set; } = "RelevantOnly";
    public string? BoundVersion { get; set; }
    public string? UsedByChaptersJson { get; set; }
    public string? UsageIdempotencyKeysJson { get; set; }
    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
}

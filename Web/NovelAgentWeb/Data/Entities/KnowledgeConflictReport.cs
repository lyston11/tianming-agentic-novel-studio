namespace TM.Web.NovelAgentWeb.Data.Entities;

public class KnowledgeConflictReport
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string KnowledgeId { get; set; } = null!;
    public string ConflictingKnowledgeIdsJson { get; set; } = "[]";
    public string ConflictType { get; set; } = "";
    public string Severity { get; set; } = "";
    public string ImpactScope { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string RecommendedAction { get; set; } = "";
    public bool RequiresUserDecision { get; set; }
    public string Status { get; set; } = "open";
    public string DetectionJson { get; set; } = "{}";
    public string? SourceSessionId { get; set; }
    public string? SourceRunId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
    public string? ResolvedBySessionId { get; set; }
    public string? ResolvedByRunId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
    public KnowledgeBase Knowledge { get; set; } = null!;
}

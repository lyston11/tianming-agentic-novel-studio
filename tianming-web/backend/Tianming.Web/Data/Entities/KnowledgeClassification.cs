namespace TM.Web.NovelAgentWeb.Data.Entities;

public class KnowledgeClassification
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string KnowledgeId { get; set; } = null!;
    public string Model { get; set; } = "";
    public string ClassificationJson { get; set; } = "{}";
    public string Role { get; set; } = "";
    public string Scope { get; set; } = "";
    public int Priority { get; set; }
    public string ConstraintLevel { get; set; } = "";
    public string PackagePolicy { get; set; } = "";
    public double Confidence { get; set; }
    public string? SourceSessionId { get; set; }
    public string? SourceRunId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
    public KnowledgeBase Knowledge { get; set; } = null!;
}

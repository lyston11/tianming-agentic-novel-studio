namespace TM.Web.NovelAgentWeb.Data.Entities;

public class TianmingPackage
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string? ChapterId { get; set; }
    public string? RuntimeRunId { get; set; }
    public string PackageKind { get; set; } = "chapter_generation";
    public string Status { get; set; } = "pending";
    public string InputJson { get; set; } = "{}";
    public string? DependencyVersionsJson { get; set; }
    public string? KnowledgeSnapshotJson { get; set; }
    public string? FactSnapshotJson { get; set; }
    public string? PromptVersion { get; set; }
    public string? KernelVersion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public NovelProject Project { get; set; } = null!;
    public Chapter? Chapter { get; set; }
}

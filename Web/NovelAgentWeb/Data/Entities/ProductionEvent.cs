namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ProductionEvent
{
    public string Id { get; set; } = null!;
    public string RuntimeRunId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string? ChapterId { get; set; }
    public string? PackageId { get; set; }
    public string EventType { get; set; } = null!;
    public string Stage { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? ArtifactType { get; set; }
    public string? ArtifactId { get; set; }
    public string? DataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public NovelProject Project { get; set; } = null!;
    public Chapter? Chapter { get; set; }
}

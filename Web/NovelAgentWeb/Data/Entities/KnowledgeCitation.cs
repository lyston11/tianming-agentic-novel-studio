namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class KnowledgeCitation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string KnowledgeEntryId { get; set; } = string.Empty;
    public long KnowledgeVersion { get; set; }
    public string? GoalId { get; set; }
    public string? ChapterId { get; set; }
    public string? ChapterVersionId { get; set; }
    public string Purpose { get; set; } = "reference";
    public string SourceArtifactId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ChapterDraft
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string RuntimeRunId { get; set; } = null!;
    public string ChapterId { get; set; } = null!;
    public string? PackageId { get; set; }
    public string ArtifactId { get; set; } = null!;
    public string Status { get; set; } = "draft_generated";
    public string DraftContent { get; set; } = string.Empty;
    public string? ChangesJson { get; set; }
    public int ContentLength { get; set; }
    public int RepairAttemptCount { get; set; }
    public bool HasChanges { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public NovelProject Project { get; set; } = null!;
    public Chapter? Chapter { get; set; }
}

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ProjectFactSnapshot
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string? ChapterId { get; set; }
    public string? ChapterVersionId { get; set; }
    public int VersionNumber { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string Source { get; set; } = "unknown";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public NovelProject Project { get; set; } = null!;
    public Chapter? Chapter { get; set; }
    public ChapterVersion? ChapterVersion { get; set; }
}

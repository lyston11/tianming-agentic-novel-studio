namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ChapterChange
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string RuntimeRunId { get; set; } = null!;
    public string ChapterId { get; set; } = null!;
    public string? PackageId { get; set; }
    public string ChangesJson { get; set; } = "{}";
    public string CanonicalChangesJson { get; set; } = "{}";
    public string ParseStatus { get; set; } = "unknown";
    public string? ParseError { get; set; }
    public bool AppliedToFactSnapshot { get; set; }
    public DateTime? AppliedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public NovelProject Project { get; set; } = null!;
    public Chapter? Chapter { get; set; }
}

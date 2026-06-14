namespace TM.Web.NovelAgentWeb.Data.Entities;

public class Chapter
{
    public string Id { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string? VolumeId { get; set; }
    public string Title { get; set; } = null!;
    public int ChapterNumber { get; set; }
    public int WordCount { get; set; } = 0;
    public string Status { get; set; } = "draft";
    public string? ContentDocumentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public NovelProject Project { get; set; } = null!;
    public Volume? Volume { get; set; }
    public ContentDocument? ContentDocument { get; set; }
    public ICollection<Foreshadow> ForeshadowsSetup { get; set; } = new List<Foreshadow>();
    public ICollection<Foreshadow> ForeshadowsPayoff { get; set; } = new List<Foreshadow>();
}

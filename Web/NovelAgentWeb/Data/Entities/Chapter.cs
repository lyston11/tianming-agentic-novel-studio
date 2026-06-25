namespace TM.Web.NovelAgentWeb.Data.Entities;

public class Chapter
{
    public string Id { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string? VolumeId { get; set; }
    public string Title { get; set; } = null!;
    public int ChapterNumber { get; set; }
    public int WordCount { get; set; } = 0;
    public string? CurrentDocumentId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public NovelProject Project { get; set; } = null!;
    public Volume? Volume { get; set; }
    public ICollection<ChapterVersion> Versions { get; set; } = new List<ChapterVersion>();
    public ICollection<Foreshadow> ForeshadowsSetup { get; set; } = new List<Foreshadow>();
    public ICollection<Foreshadow> ForeshadowsPayoff { get; set; } = new List<Foreshadow>();
}

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class Foreshadow
{
    public string Id { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Type { get; set; }
    public string Status { get; set; } = "planned";
    public string? SetupChapterId { get; set; }
    public string? PayoffChapterId { get; set; }
    public int? Importance { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public NovelProject Project { get; set; } = null!;
    public Chapter? SetupChapter { get; set; }
    public Chapter? PayoffChapter { get; set; }
}

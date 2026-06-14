namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ContentDocument
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string SourceType { get; set; } = null!;   // chapter | material | knowledge_upload
    public string SourceId { get; set; } = null!;      // Chapter.Id / Material.Id / KnowledgeProcessingTask.Id
    public string DocumentRole { get; set; } = null!;  // chapter_body | material_raw | upload_raw
    public string Title { get; set; } = null!;
    public string MimeType { get; set; } = "text/plain";
    public string ContentHash { get; set; } = null!;
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
    public ICollection<ContentChunk> Chunks { get; set; } = new List<ContentChunk>();
}

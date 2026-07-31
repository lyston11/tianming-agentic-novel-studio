namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class KnowledgeSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string DocumentBlobId { get; set; } = string.Empty;
    public long KnowledgeVersion { get; set; }
    public int SectionIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

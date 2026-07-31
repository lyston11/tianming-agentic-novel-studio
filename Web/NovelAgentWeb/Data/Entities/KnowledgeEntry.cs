namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class KnowledgeEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string LogicalKnowledgeId { get; set; } = string.Empty;
    public string DocumentBlobId { get; set; } = string.Empty;
    public long KnowledgeVersion { get; set; }
    public int Version { get; set; } = 1;
    public int SourceEntryIndex { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

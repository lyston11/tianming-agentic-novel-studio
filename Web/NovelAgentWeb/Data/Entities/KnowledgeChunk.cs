namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class KnowledgeChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string DocumentBlobId { get; set; } = string.Empty;
    public string SectionId { get; set; } = string.Empty;
    public long KnowledgeVersion { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string? PreviousChunkId { get; set; }
    public string? NextChunkId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

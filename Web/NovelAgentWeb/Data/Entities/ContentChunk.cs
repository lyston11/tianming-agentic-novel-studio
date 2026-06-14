namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ContentChunk
{
    public string Id { get; set; } = null!;
    public string DocumentId { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = null!;
    public int TokenCount { get; set; }
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
    public string ContentHash { get; set; } = null!;
    public ContentDocument Document { get; set; } = null!;
}

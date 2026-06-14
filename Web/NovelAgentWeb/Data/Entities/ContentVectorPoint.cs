namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ContentVectorPoint
{
    public string Id { get; set; } = null!;
    public string DocumentId { get; set; } = null!;
    public string? ChunkId { get; set; }
    public string QdrantCollection { get; set; } = null!;
    public string QdrantPointId { get; set; } = null!;
    public string VectorModel { get; set; } = null!;
    public DateTime? IndexedAt { get; set; }
    public string IndexStatus { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public ContentDocument Document { get; set; } = null!;
}

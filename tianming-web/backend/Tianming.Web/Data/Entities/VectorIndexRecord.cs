namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class VectorIndexRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public string? DocumentBlobId { get; set; }
    public string SourceDocumentId { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public int? ChunkIndex { get; set; }
    public long KnowledgeVersion { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string EmbeddingVersion { get; set; } = string.Empty;
    public string QdrantCollection { get; set; } = string.Empty;
    public string QdrantPointId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public DateTime? IndexedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class Material
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string Title { get; set; } = null!;
    public string? Category { get; set; }
    public string? ContentType { get; set; }
    public string? Tags { get; set; }
    public string? RawDocumentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("vector_chunk_count")]
    public int VectorChunkCount { get; set; } = 0;

    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
}

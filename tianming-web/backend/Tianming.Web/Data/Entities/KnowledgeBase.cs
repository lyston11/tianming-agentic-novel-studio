using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class KnowledgeBase
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? SourceProjectId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string EntryType { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
    public int UsageCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("is_archived")]
    public bool IsArchived { get; set; } = false;

    [Column("vector_id")]
    [StringLength(100)]
    public string? VectorId { get; set; }

    // Source tracking fields for knowledge extraction
    [Column("source_type")]
    [StringLength(50)]
    public string SourceType { get; set; } = "manual";

    [Column("source_upload_task_id")]
    [StringLength(100)]
    public string? SourceUploadTaskId { get; set; }

    [Column("chunk_index")]
    public int? ChunkIndex { get; set; }

    [Column("extraction_context")]
    public string? ExtractionContext { get; set; }

    // Metadata fields for extracted knowledge
    [Column("tags")]
    public string? Tags { get; set; }

    [Column("weight")]
    public int Weight { get; set; } = 5;

    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject? SourceProject { get; set; }
}

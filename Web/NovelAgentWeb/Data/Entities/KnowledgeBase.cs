using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class KnowledgeBase
{
    public string Id { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string EntryType { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
    public int UsageCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("vector_id")]
    [StringLength(100)]
    public string? VectorId { get; set; }

    // Source tracking fields for knowledge extraction
    [Column("source_type")]
    [StringLength(50)]
    public string SourceType { get; set; } = "manual";

    [Column("source_file_id")]
    [StringLength(100)]
    public string? SourceFileId { get; set; }

    [Column("chunk_index")]
    public int? ChunkIndex { get; set; }

    [Column("extraction_context")]
    public string? ExtractionContext { get; set; }

    // Metadata fields for extracted knowledge
    [Column("tags")]
    public string? Tags { get; set; }

    [Column("weight")]
    public int Weight { get; set; } = 5;

    // Navigation property
    public NovelProject Project { get; set; } = null!;
}

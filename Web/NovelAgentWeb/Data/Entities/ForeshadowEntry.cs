using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("foreshadow_ledger")]
public sealed class ForeshadowEntry
{
    [Key]
    [Column("id")]
    [StringLength(50)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    [Column("user_id")]
    [StringLength(50)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [Column("project_id")]
    [StringLength(50)]
    public string ProjectId { get; set; } = string.Empty;

    [Required]
    [Column("title")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Required]
    [Column("category")]
    [StringLength(100)]
    public string Category { get; set; } = string.Empty; // Expected values: "plot", "character", "world", "theme", etc.

    [Required]
    [Column("planted_in_chapter")]
    [StringLength(50)]
    public string PlantedInChapter { get; set; } = string.Empty;

    [Column("planted_context")]
    public string? PlantedContext { get; set; }

    [Required]
    [Column("status")]
    [StringLength(50)]
    public string Status { get; set; } = "planted"; // Expected values: "planted", "developing", "resolved"

    [Column("resolved_in_chapter")]
    [StringLength(50)]
    public string? ResolvedInChapter { get; set; }

    [Column("resolved_context")]
    public string? ResolvedContext { get; set; }

    [Required]
    [Column("planted_at")]
    public DateTime PlantedAt { get; set; }

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }

    [Required]
    [Column("priority")]
    [Range(1, 10)]
    public int Priority { get; set; } = 5;

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Required]
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(ProjectId))]
    public NovelProject? Project { get; set; }
}

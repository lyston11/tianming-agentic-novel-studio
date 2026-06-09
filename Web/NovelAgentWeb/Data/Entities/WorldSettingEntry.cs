using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("world_settings")]
public sealed class WorldSettingEntry
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
    [Column("category")]
    [StringLength(100)]
    public string Category { get; set; } = string.Empty; // Expected values: "location", "power_system", "culture", "history", "other"

    [Column("sub_category")]
    [StringLength(100)]
    public string? SubCategory { get; set; }

    [Required]
    [Column("title")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("first_mentioned_chapter")]
    [StringLength(50)]
    public string? FirstMentionedChapter { get; set; }

    [Column("referenced_chapters")]
    public string? ReferencedChapters { get; set; } // JSON array of chapter IDs

    [Required]
    [Column("version")]
    public int Version { get; set; } = 1;

    [Column("previous_version")]
    [StringLength(50)]
    public string? PreviousVersion { get; set; }

    [Column("change_log")]
    public string? ChangeLog { get; set; }

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

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("volume_arcs")]
public sealed class VolumeArc
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
    [Column("volume_number")]
    public int VolumeNumber { get; set; }

    [Required]
    [Column("volume_title")]
    [StringLength(200)]
    public string VolumeTitle { get; set; } = string.Empty;

    [Column("volume_theme")]
    [StringLength(500)]
    public string? VolumeTheme { get; set; }

    [Column("target_chapters")]
    public int? TargetChapters { get; set; }

    [Required]
    [Column("current_chapters")]
    public int CurrentChapters { get; set; }

    [Column("act1_setup")]
    public string? Act1Setup { get; set; }

    [Column("act2_confrontation")]
    public string? Act2Confrontation { get; set; }

    [Column("act3_climax")]
    public string? Act3Climax { get; set; }

    [Column("act4_resolution")]
    public string? Act4Resolution { get; set; }

    [Column("key_events")]
    public string? KeyEvents { get; set; }

    [Column("major_conflict")]
    public string? MajorConflict { get; set; }

    [Column("conflict_escalation")]
    public string? ConflictEscalation { get; set; }

    [Required]
    [Column("status")]
    [StringLength(50)]
    public string Status { get; set; } = string.Empty; // Expected values: "planned", "in_progress", "completed"

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Required]
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(ProjectId))]
    public NovelProject? Project { get; set; }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("characters")]
public sealed class Character
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

    [Column("idempotency_key")]
    [StringLength(160)]
    public string? IdempotencyKey { get; set; }

    [Required]
    [Column("name")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("role")]
    [StringLength(100)]
    public string Role { get; set; } = string.Empty; // Expected values: "protagonist", "antagonist", "supporting", "minor"

    [Column("alias")]
    [StringLength(200)]
    public string? Alias { get; set; }

    [Column("age")]
    public int? Age { get; set; }

    [Column("gender")]
    [StringLength(50)]
    public string? Gender { get; set; }

    [Column("appearance")]
    [StringLength(2000)]
    public string? Appearance { get; set; }

    [Column("personality")]
    [StringLength(2000)]
    public string? Personality { get; set; }

    [Column("background")]
    public string? Background { get; set; }

    [Column("initial_power_level")]
    [StringLength(200)]
    public string? InitialPowerLevel { get; set; }

    [Column("current_power_level")]
    [StringLength(200)]
    public string? CurrentPowerLevel { get; set; }

    [Column("special_abilities")]
    [StringLength(2000)]
    public string? SpecialAbilities { get; set; }

    [Column("core_goal")]
    [StringLength(1000)]
    public string? CoreGoal { get; set; }

    [Column("motivation")]
    [StringLength(2000)]
    public string? Motivation { get; set; }

    [Column("relationships")]
    public string? Relationships { get; set; } // JSON serialized relationship data

    [Required]
    [Column("status")]
    [StringLength(50)]
    public string Status { get; set; } = "active"; // Expected values: "active", "inactive", "deceased"

    [Column("first_appear_chapter")]
    [StringLength(50)]
    public string? FirstAppearChapter { get; set; }

    [Column("last_appear_chapter")]
    [StringLength(50)]
    public string? LastAppearChapter { get; set; }

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

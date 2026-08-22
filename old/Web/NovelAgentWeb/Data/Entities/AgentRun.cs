using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("agent_runs")]
public sealed class AgentRun
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
    [Column("run_type")]
    [StringLength(100)]
    public string RunType { get; set; } = string.Empty; // Expected values: "chapter_generation", "outline_generation", "character_development", etc.

    [Column("target_chapter_id")]
    [StringLength(50)]
    public string? TargetChapterId { get; set; }

    [Required]
    [Column("status")]
    [StringLength(50)]
    public string Status { get; set; } = "running"; // Expected values: "running", "completed", "failed"

    [Column("input_params")]
    public string? InputParams { get; set; } // JSON serialized input parameters

    [Column("output_data")]
    public string? OutputData { get; set; } // JSON serialized output data

    [Column("output_document_id")]
    [StringLength(50)]
    public string? OutputDocumentId { get; set; }

    [Column("context_package_size")]
    public int? ContextPackageSize { get; set; }

    [Required]
    [Column("started_at")]
    public DateTime StartedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("duration_ms")]
    public int? DurationMs { get; set; }

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

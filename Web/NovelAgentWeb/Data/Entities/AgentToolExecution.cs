using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("agent_tool_executions")]
public sealed class AgentToolExecution
{
    [Key]
    [Column("id")]
    [StringLength(50)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    [Column("user_id")]
    [StringLength(50)]
    public string UserId { get; set; } = string.Empty;

    [Column("project_id")]
    [StringLength(50)]
    public string? ProjectId { get; set; }

    [Required]
    [Column("session_id")]
    [StringLength(50)]
    public string SessionId { get; set; } = string.Empty;

    [Column("run_id")]
    [StringLength(50)]
    public string? RunId { get; set; }

    [Required]
    [Column("tool_name")]
    [StringLength(120)]
    public string ToolName { get; set; } = string.Empty;

    [Column("phase")]
    [StringLength(80)]
    public string Phase { get; set; } = string.Empty;

    [Column("risk")]
    [StringLength(30)]
    public string Risk { get; set; } = "Low";

    [Column("arguments_json")]
    public string ArgumentsJson { get; set; } = "{}";

    [Required]
    [Column("arguments_hash")]
    [StringLength(64)]
    public string ArgumentsHash { get; set; } = string.Empty;

    [Column("side_effects_json")]
    public string SideEffectsJson { get; set; } = "{}";

    [Required]
    [Column("status")]
    [StringLength(30)]
    public string Status { get; set; } = "running";

    [Column("result_phase")]
    [StringLength(80)]
    public string ResultPhase { get; set; } = string.Empty;

    [Column("result_message")]
    public string ResultMessage { get; set; } = string.Empty;

    [Column("error_type")]
    [StringLength(80)]
    public string ErrorType { get; set; } = string.Empty;

    [Column("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [Column("recommended_next_tool")]
    [StringLength(120)]
    public string RecommendedNextTool { get; set; } = string.Empty;

    [Column("missing_prerequisite")]
    [StringLength(120)]
    public string MissingPrerequisite { get; set; } = string.Empty;

    [Required]
    [Column("started_at")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("duration_ms")]
    public int? DurationMs { get; set; }
}

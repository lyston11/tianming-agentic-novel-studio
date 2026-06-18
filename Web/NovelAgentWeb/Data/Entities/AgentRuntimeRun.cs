using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("agent_runtime_runs")]
public sealed class AgentRuntimeRun
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
    [Column("session_id")]
    [StringLength(50)]
    public string SessionId { get; set; } = string.Empty;

    [Column("project_id")]
    [StringLength(50)]
    public string? ProjectId { get; set; }

    [Required]
    [Column("status")]
    [StringLength(30)]
    public string Status { get; set; } = "queued";

    [Column("current_phase")]
    [StringLength(80)]
    public string CurrentPhase { get; set; } = "queued";

    [Column("current_step")]
    public int CurrentStep { get; set; }

    [Column("active_tool")]
    [StringLength(120)]
    public string ActiveTool { get; set; } = string.Empty;

    [Column("user_message")]
    public string UserMessage { get; set; } = string.Empty;

    [Column("last_message")]
    public string LastMessage { get; set; } = string.Empty;

    [Column("result_json")]
    public string ResultJson { get; set; } = "{}";

    [Column("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [Column("cancel_requested")]
    public bool CancelRequested { get; set; }

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

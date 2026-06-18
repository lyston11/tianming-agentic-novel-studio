using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("agent_interrupts")]
public sealed class AgentInterrupt
{
    [Key]
    [Column("id")]
    [StringLength(50)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    [Column("runtime_run_id")]
    [StringLength(50)]
    public string RuntimeRunId { get; set; } = string.Empty;

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
    [Column("kind")]
    [StringLength(40)]
    public string Kind { get; set; } = "freeform";

    [Required]
    [Column("status")]
    [StringLength(30)]
    public string Status { get; set; } = "pending";

    [Column("priority")]
    public int Priority { get; set; }

    [Required]
    [Column("message")]
    public string Message { get; set; } = string.Empty;

    [Column("decision_json")]
    public string DecisionJson { get; set; } = "{}";

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("consumed_at")]
    public DateTime? ConsumedAt { get; set; }
}

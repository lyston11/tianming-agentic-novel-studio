using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("agent_runtime_events")]
public sealed class AgentRuntimeEvent
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
    [Column("type")]
    [StringLength(80)]
    public string Type { get; set; } = string.Empty;

    [Column("stage")]
    [StringLength(80)]
    public string Stage { get; set; } = string.Empty;

    [Column("status")]
    [StringLength(40)]
    public string Status { get; set; } = string.Empty;

    [Column("artifact_type")]
    [StringLength(80)]
    public string ArtifactType { get; set; } = string.Empty;

    [Column("artifact_id")]
    [StringLength(80)]
    public string ArtifactId { get; set; } = string.Empty;

    [Column("display_surface")]
    [StringLength(40)]
    public string DisplaySurface { get; set; } = "chat";

    [Column("display_policy")]
    [StringLength(40)]
    public string DisplayPolicy { get; set; } = "collapsible";

    [Required]
    [Column("message")]
    public string Message { get; set; } = string.Empty;

    [Column("data_json")]
    public string DataJson { get; set; } = "{}";

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

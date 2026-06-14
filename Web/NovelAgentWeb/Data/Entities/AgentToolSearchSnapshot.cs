using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("agent_tool_search_snapshots")]
public sealed class AgentToolSearchSnapshot
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

    [Required]
    [Column("phase")]
    [StringLength(80)]
    public string Phase { get; set; } = string.Empty;

    [Required]
    [Column("version")]
    [StringLength(500)]
    public string Version { get; set; } = string.Empty;

    [Required]
    [Column("tools_json")]
    public string ToolsJson { get; set; } = "[]";

    [Column("source_execution_id")]
    [StringLength(50)]
    public string SourceExecutionId { get; set; } = string.Empty;

    [Required]
    [Column("cached_at")]
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;

    [Required]
    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow;
}

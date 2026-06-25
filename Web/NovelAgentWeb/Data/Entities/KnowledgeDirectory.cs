using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class KnowledgeDirectory
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;

    [StringLength(120)]
    public string Key { get; set; } = null!;

    [StringLength(120)]
    public string Name { get; set; } = null!;

    [StringLength(160)]
    public string? IdempotencyKey { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

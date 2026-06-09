using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

[Table("story_constitutions")]
public sealed class StoryConstitution
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
    [Column("genre")]
    [StringLength(100)]
    public string Genre { get; set; } = string.Empty;

    [Column("sub_genre")]
    [StringLength(100)]
    public string? SubGenre { get; set; }

    [Required]
    [Column("core_hook")]
    [StringLength(1000)]
    public string CoreHook { get; set; } = string.Empty;

    [Column("reader_promise")]
    [StringLength(1000)]
    public string? ReaderPromise { get; set; }

    [Column("genre_profile")]
    public string? GenreProfile { get; set; }

    [Column("target_audience")]
    [StringLength(500)]
    public string? TargetAudience { get; set; }

    [Column("taboos")]
    public string? Taboos { get; set; }

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

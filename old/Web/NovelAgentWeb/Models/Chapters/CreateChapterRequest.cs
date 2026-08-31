using System.ComponentModel.DataAnnotations;

namespace TM.Web.NovelAgentWeb.Models.Chapters;

/// <summary>
/// Request model for creating a new chapter.
/// </summary>
public class CreateChapterRequest
{
    /// <summary>
    /// Project ID that this chapter belongs to (required).
    /// </summary>
    [Required(ErrorMessage = "ProjectId is required")]
    public required string ProjectId { get; set; }

    /// <summary>
    /// Volume ID that this chapter belongs to (optional).
    /// </summary>
    public string? VolumeId { get; set; }

    /// <summary>
    /// Chapter title (required, max 200 characters).
    /// </summary>
    [Required(ErrorMessage = "Title is required")]
    [StringLength(200, ErrorMessage = "Title must be 200 characters or less")]
    public required string Title { get; set; }

    /// <summary>
    /// Chapter number (required, must be positive).
    /// </summary>
    [Required(ErrorMessage = "ChapterNumber is required")]
    [Range(1, int.MaxValue, ErrorMessage = "ChapterNumber must be a positive integer")]
    public required int ChapterNumber { get; set; }

    /// <summary>
    /// Chapter content in Markdown format (required).
    /// </summary>
    [Required(ErrorMessage = "Content is required")]
    public required string Content { get; set; }

    /// <summary>
    /// Chapter status (optional, default: "draft").
    /// Valid values: "draft", "published", "archived".
    /// </summary>
    [RegularExpression("^(draft|published|archived)$", ErrorMessage = "Status must be 'draft', 'published', or 'archived'")]
    public string Status { get; set; } = "draft";

    public string? IdempotencyKey { get; set; }
}

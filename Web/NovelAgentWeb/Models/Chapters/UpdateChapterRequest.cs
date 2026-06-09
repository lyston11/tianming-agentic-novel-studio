using System.ComponentModel.DataAnnotations;

namespace TM.Web.NovelAgentWeb.Models.Chapters;

/// <summary>
/// Request model for updating an existing chapter.
/// </summary>
public class UpdateChapterRequest
{
    /// <summary>
    /// Updated chapter title (optional, max 200 characters).
    /// </summary>
    [StringLength(200, ErrorMessage = "Title must be 200 characters or less")]
    public string? Title { get; set; }

    /// <summary>
    /// Updated chapter content in Markdown format (optional).
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Updated chapter status (optional).
    /// Valid values: "draft", "published", "archived".
    /// </summary>
    [RegularExpression("^(draft|published|archived)$", ErrorMessage = "Status must be 'draft', 'published', or 'archived'")]
    public string? Status { get; set; }

    /// <summary>
    /// Updated volume ID (optional, can be null to remove volume association).
    /// </summary>
    public string? VolumeId { get; set; }
}

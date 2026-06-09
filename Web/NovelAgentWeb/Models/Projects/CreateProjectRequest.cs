using System.ComponentModel.DataAnnotations;

namespace TM.Web.NovelAgentWeb.Models.Projects;

/// <summary>
/// Request model for creating a new novel project.
/// </summary>
public class CreateProjectRequest
{
    /// <summary>
    /// Project title (required, max 200 characters).
    /// </summary>
    [Required(ErrorMessage = "Title is required")]
    [StringLength(200, ErrorMessage = "Title must be 200 characters or less")]
    public required string Title { get; set; }

    /// <summary>
    /// Project genre (optional, e.g., "玄幻", "都市", "科幻").
    /// </summary>
    [StringLength(50, ErrorMessage = "Genre must be 50 characters or less")]
    public string? Genre { get; set; }

    /// <summary>
    /// Project sub-genre (optional).
    /// </summary>
    [StringLength(50, ErrorMessage = "SubGenre must be 50 characters or less")]
    public string? SubGenre { get; set; }

    /// <summary>
    /// Core hook/premise of the novel (optional).
    /// </summary>
    [StringLength(500, ErrorMessage = "CoreHook must be 500 characters or less")]
    public string? CoreHook { get; set; }

    /// <summary>
    /// Cover image URL (optional).
    /// </summary>
    [Url(ErrorMessage = "CoverImageUrl must be a valid URL")]
    public string? CoverImageUrl { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace TM.Web.NovelAgentWeb.Models.Projects;

/// <summary>
/// Request model for updating an existing novel project.
/// All fields are optional - only provided fields will be updated.
/// </summary>
public class UpdateProjectRequest
{
    /// <summary>
    /// Updated project title (optional).
    /// </summary>
    [StringLength(200, ErrorMessage = "Title must be 200 characters or less")]
    public string? Title { get; set; }

    /// <summary>
    /// Updated project genre (optional).
    /// </summary>
    [StringLength(50, ErrorMessage = "Genre must be 50 characters or less")]
    public string? Genre { get; set; }

    /// <summary>
    /// Updated project sub-genre (optional).
    /// </summary>
    [StringLength(50, ErrorMessage = "SubGenre must be 50 characters or less")]
    public string? SubGenre { get; set; }

    /// <summary>
    /// Updated core hook/premise (optional).
    /// </summary>
    [StringLength(500, ErrorMessage = "CoreHook must be 500 characters or less")]
    public string? CoreHook { get; set; }

    /// <summary>
    /// Updated project status (optional, e.g., "draft", "active", "completed", "archived").
    /// </summary>
    [StringLength(20, ErrorMessage = "Status must be 20 characters or less")]
    public string? Status { get; set; }

    /// <summary>
    /// Updated cover image URL (optional).
    /// </summary>
    [Url(ErrorMessage = "CoverImageUrl must be a valid URL")]
    public string? CoverImageUrl { get; set; }
}

namespace TM.Web.NovelAgentWeb.Models.Projects;

/// <summary>
/// Response model for project data returned by API endpoints.
/// </summary>
public class ProjectResponse
{
    /// <summary>
    /// Unique project identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Owner user ID.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Project title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Project genre.
    /// </summary>
    public string? Genre { get; set; }

    /// <summary>
    /// Project sub-genre.
    /// </summary>
    public string? SubGenre { get; set; }

    /// <summary>
    /// Core hook/premise of the novel.
    /// </summary>
    public string? CoreHook { get; set; }

    /// <summary>
    /// Project status (e.g., "draft", "active", "completed", "archived").
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Total word count across all chapters.
    /// </summary>
    public int WordCount { get; set; }

    /// <summary>
    /// Cover image URL.
    /// </summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// Project creation timestamp (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Project last update timestamp (UTC).
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

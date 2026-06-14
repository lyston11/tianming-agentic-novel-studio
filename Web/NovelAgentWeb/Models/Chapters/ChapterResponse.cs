namespace TM.Web.NovelAgentWeb.Models.Chapters;

/// <summary>
/// Response model for chapter data.
/// </summary>
public class ChapterResponse
{
    /// <summary>
    /// Unique chapter identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Project ID that this chapter belongs to.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Volume ID that this chapter belongs to (if any).
    /// </summary>
    public string? VolumeId { get; set; }

    /// <summary>
    /// Chapter title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Chapter number within the project.
    /// </summary>
    public required int ChapterNumber { get; set; }

    /// <summary>
    /// Chapter status (draft, published, archived).
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Word count of the chapter content.
    /// </summary>
    public required int WordCount { get; set; }

    /// <summary>
    /// Chapter content in Markdown format (only included in GetById).
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public required DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public required DateTime UpdatedAt { get; set; }
}

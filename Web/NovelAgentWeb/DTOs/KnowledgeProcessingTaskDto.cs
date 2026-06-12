namespace TM.Web.NovelAgentWeb.DTOs;

/// <summary>
/// Response DTO for knowledge processing task status.
/// Used to return file processing task information to the frontend.
/// </summary>
public class KnowledgeProcessingTaskDto
{
    /// <summary>
    /// Unique task identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Original filename of the uploaded file.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Current task status (Pending, Processing, Completed, Failed).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Processing strategy used (ShortFile, LongFileChunked).
    /// </summary>
    public string Strategy { get; set; } = string.Empty;

    /// <summary>
    /// Overall progress percentage (0-100).
    /// </summary>
    public int Progress { get; set; }

    /// <summary>
    /// Total number of chunks for long file processing (null for short files).
    /// </summary>
    public int? TotalChunks { get; set; }

    /// <summary>
    /// Number of chunks processed so far.
    /// </summary>
    public int ProcessedChunks { get; set; }

    /// <summary>
    /// Number of knowledge entries extracted and saved.
    /// </summary>
    public int ExtractedEntriesCount { get; set; }

    /// <summary>
    /// Error message if task failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Timestamp when processing started.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Timestamp when processing completed (successfully or with failure).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Timestamp when task was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

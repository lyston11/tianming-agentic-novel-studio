using TM.Web.NovelAgentWeb.Models.Chapters;

namespace TM.Web.NovelAgentWeb.Services.Chapters;

/// <summary>
/// Service interface for chapter CRUD operations backed by database content
/// documents, chapter versions, and asynchronous production outbox indexing.
/// </summary>
public interface IChapterService
{
    /// <summary>
    /// Create a new chapter with database truth records:
    /// - Insert metadata into SQLite
    /// - Persist Markdown content into SQLite content documents
    /// - Create a chapter version
    /// - Queue asynchronous indexing
    /// </summary>
    /// <param name="request">Chapter creation request</param>
    /// <param name="userId">Current user ID for ownership verification</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created chapter response</returns>
    Task<ChapterResponse> CreateChapterAsync(
        CreateChapterRequest request,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing chapter with database truth records:
    /// - Update metadata in SQLite
    /// - Update SQLite content document if content changed
    /// - Create a new chapter version and queue asynchronous indexing if content changed
    /// </summary>
    /// <param name="chapterId">Chapter ID to update</param>
    /// <param name="request">Chapter update request</param>
    /// <param name="userId">Current user ID for ownership verification</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated chapter response</returns>
    Task<ChapterResponse> UpdateChapterAsync(
        string chapterId,
        UpdateChapterRequest request,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a chapter by ID with content.
    /// Verifies user ownership before returning data.
    /// </summary>
    /// <param name="chapterId">Chapter ID</param>
    /// <param name="userId">Current user ID for ownership verification</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chapter response with content</returns>
    Task<ChapterResponse> GetChapterByIdAsync(
        string chapterId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all persisted versions for a chapter, including production package
    /// lineage needed by workflow, Agent tools, and future rollback flows.
    /// </summary>
    /// <param name="chapterId">Chapter ID</param>
    /// <param name="userId">Current user ID for ownership verification</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chapter versions ordered from latest to oldest</returns>
    Task<List<ChapterVersionResponse>> GetChapterVersionsAsync(
        string chapterId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two persisted chapter versions using their own content documents.
    /// </summary>
    /// <param name="chapterId">Chapter ID</param>
    /// <param name="leftVersionId">Left/baseline version ID</param>
    /// <param name="rightVersionId">Right/target version ID</param>
    /// <param name="userId">Current user ID for ownership verification</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paragraph-level version comparison and lineage</returns>
    Task<ChapterVersionCompareResponse> CompareChapterVersionsAsync(
        string chapterId,
        string leftVersionId,
        string rightVersionId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a chapter with database truth cleanup:
    /// - Delete metadata from SQLite (cascades to FK references)
    /// - Delete SQLite content document
    /// - Queue asynchronous index cleanup
    /// Foreign key references (foreshadows) are automatically set to NULL.
    /// </summary>
    /// <param name="chapterId">Chapter ID to delete</param>
    /// <param name="userId">Current user ID for ownership verification</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteChapterAsync(
        string chapterId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all chapters for a project.
    /// Verifies project ownership before returning data.
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="userId">Current user ID for ownership verification</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of chapters without content</returns>
    Task<List<ChapterResponse>> GetChaptersByProjectAsync(
        string projectId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);
}

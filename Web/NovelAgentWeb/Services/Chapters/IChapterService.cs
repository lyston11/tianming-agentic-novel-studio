using TM.Web.NovelAgentWeb.Models.Chapters;

namespace TM.Web.NovelAgentWeb.Services.Chapters;

/// <summary>
/// Service interface for chapter CRUD operations with synchronization across
/// SQLite (metadata), file system (Markdown content), and Qdrant (vectors).
/// </summary>
public interface IChapterService
{
    /// <summary>
    /// Create a new chapter with atomic synchronization:
    /// - Insert metadata into SQLite
    /// - Write Markdown file to file system
    /// - Generate and store embeddings in Qdrant
    /// All operations succeed together or roll back.
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
    /// Update an existing chapter with atomic synchronization:
    /// - Update metadata in SQLite
    /// - Update Markdown file if content changed
    /// - Regenerate and update embeddings in Qdrant if content changed
    /// All operations succeed together or roll back.
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
    /// Delete a chapter with atomic synchronization:
    /// - Delete metadata from SQLite (cascades to FK references)
    /// - Delete Markdown file from file system
    /// - Delete embeddings from Qdrant
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

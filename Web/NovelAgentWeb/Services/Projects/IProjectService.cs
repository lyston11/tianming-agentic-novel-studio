using TM.Web.NovelAgentWeb.Models.Common;
using TM.Web.NovelAgentWeb.Models.Projects;

namespace TM.Web.NovelAgentWeb.Services.Projects;

/// <summary>
/// Service interface for managing novel projects with user isolation.
/// Enforces authorization and provides CRUD operations.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Get a paginated list of projects for the current user.
    /// Regular users see only their own projects; admins can see all projects.
    /// </summary>
    /// <param name="userId">Current user ID</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="pageNumber">Page number (1-based, default: 1)</param>
    /// <param name="pageSize">Page size (default: 20, max: 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged response with project list and metadata</returns>
    Task<PagedResponse<ProjectResponse>> GetUserProjectsAsync(
        string userId,
        bool isAdmin,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single project by ID with ownership verification.
    /// Regular users can only access their own projects; admins can access any project.
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="userId">Current user ID</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Project response</returns>
    /// <exception cref="KeyNotFoundException">Thrown when project not found</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when user doesn't own the project</exception>
    Task<ProjectResponse> GetProjectByIdAsync(
        string projectId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new project for the current user.
    /// Automatically sets userId from JWT token and generates unique project ID.
    /// </summary>
    /// <param name="request">Project creation request</param>
    /// <param name="userId">Current user ID (from JWT)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created project response</returns>
    Task<ProjectResponse> CreateProjectAsync(
        CreateProjectRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing project with ownership verification.
    /// Only the project owner (or admin) can update the project.
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="request">Project update request</param>
    /// <param name="userId">Current user ID</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated project response</returns>
    /// <exception cref="KeyNotFoundException">Thrown when project not found</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when user doesn't own the project</exception>
    Task<ProjectResponse> UpdateProjectAsync(
        string projectId,
        UpdateProjectRequest request,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a project with ownership verification and cascade deletion.
    /// Deletes database-owned project data and enqueues asynchronous index cleanup.
    /// Only the project owner (or admin) can delete the project.
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="userId">Current user ID</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="KeyNotFoundException">Thrown when project not found</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when user doesn't own the project</exception>
    Task DeleteProjectAsync(
        string projectId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);
}

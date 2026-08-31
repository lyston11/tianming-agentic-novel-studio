using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Extensions;
using TM.Web.NovelAgentWeb.Models.Common;
using TM.Web.NovelAgentWeb.Models.Projects;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Projects;

/// <summary>
/// Implementation of project management service with user isolation and authorization.
/// </summary>
public class ProjectService : IProjectService
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;
    private static readonly TimeSpan ProjectCacheDuration = TimeSpan.FromMinutes(1);

    private readonly NovelAgentDbContext _context;
    private readonly ILogger<ProjectService> _logger;
    private readonly IMemoryCacheService _cache;
    private readonly IProductionTruthStore _truthStore;

    public ProjectService(
        NovelAgentDbContext context,
        ILogger<ProjectService> logger,
        IMemoryCacheService cache,
        IProductionTruthStore truthStore)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
        _truthStore = truthStore;
    }

    public async Task<PagedResponse<ProjectResponse>> GetUserProjectsAsync(
        string userId,
        bool isAdmin,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Validate pagination parameters
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Build query with user filter
        var query = _context.NovelProjects
            .AsNoTracking()
            .WithUserFilterIfNotAdmin(userId, isAdmin);

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Get paginated results
        var projects = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(p => MapToResponse(p))
            .ToListAsync(cancellationToken);

        return new PagedResponse<ProjectResponse>
        {
            Items = projects,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<ProjectResponse> GetProjectByIdAsync(
        string projectId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildProjectCacheKey(projectId, userId, isAdmin);

        var cachedProject = await _cache.GetOrSetAsync(
            cacheKey,
            async () =>
            {
                var project = await _context.NovelProjects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

                if (project == null)
                {
                    throw new KeyNotFoundException($"Project with ID '{projectId}' not found");
                }

                // Verify ownership (unless admin)
                if (!isAdmin && project.UserId != userId)
                {
                    throw new UnauthorizedAccessException($"User does not have access to project '{projectId}'");
                }

                _logger.LogDebug("Loaded project {ProjectId} from database", projectId);
                return MapToResponse(project);
            },
            ProjectCacheDuration,
            cancellationToken);
        return cachedProject ?? throw new KeyNotFoundException($"Project with ID '{projectId}' not found");
    }

    public async Task<ProjectResponse> CreateProjectAsync(
        CreateProjectRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = EmptyToNull(request.IdempotencyKey);
        if (idempotencyKey != null)
        {
            var existing = await FindProjectByIdempotencyKeyAsync(userId, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing != null)
                return MapToResponse(existing);
        }

        var projectId = Guid.NewGuid().ToString();

        var project = new NovelProject
        {
            Id = projectId,
            UserId = userId,
            Title = request.Title,
            Genre = request.Genre,
            SubGenre = request.SubGenre,
            CoreHook = request.CoreHook,
            Status = "draft",
            WordCount = 0,
            CoverImageUrl = request.CoverImageUrl,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.NovelProjects.Add(project);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (idempotencyKey != null)
        {
            _context.Entry(project).State = EntityState.Detached;
            var existing = await FindProjectByIdempotencyKeyAsync(userId, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing != null)
                return MapToResponse(existing);

            throw;
        }

        _logger.LogInformation("Created project {ProjectId} for user {UserId}", projectId, userId);

        return MapToResponse(project);
    }

    public async Task<ProjectResponse> UpdateProjectAsync(
        string projectId,
        UpdateProjectRequest request,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var project = await _context.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"Project with ID '{projectId}' not found");
        }

        // Verify ownership (unless admin)
        if (!isAdmin && project.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User does not have access to project '{projectId}'");
        }

        // Update only provided fields
        if (request.Title != null)
        {
            project.Title = request.Title;
        }
        if (request.Genre != null)
        {
            project.Genre = request.Genre;
        }
        if (request.SubGenre != null)
        {
            project.SubGenre = request.SubGenre;
        }
        if (request.CoreHook != null)
        {
            project.CoreHook = request.CoreHook;
        }
        if (request.Status != null)
        {
            project.Status = request.Status;
        }
        if (request.CoverImageUrl != null)
        {
            project.CoverImageUrl = request.CoverImageUrl;
        }

        project.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate all per-user/admin views for this project.
        _cache.RemoveByPrefix(BuildProjectCachePrefix(projectId));

        _logger.LogInformation("Updated project {ProjectId} by user {UserId} and invalidated cache", projectId, userId);

        return MapToResponse(project);
    }

    public async Task DeleteProjectAsync(
        string projectId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        // Use a transaction to ensure atomicity
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var project = await _context.NovelProjects
                .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

            if (project == null)
            {
                throw new KeyNotFoundException($"Project with ID '{projectId}' not found");
            }

            // Verify ownership (unless admin)
            if (!isAdmin && project.UserId != userId)
            {
                throw new UnauthorizedAccessException($"User does not have access to project '{projectId}'");
            }

            await _truthStore.EnqueueOutboxAsync(
                new EnqueueOutboxEventRequest(
                    UserId: project.UserId,
                    ProjectId: project.Id,
                    RuntimeRunId: null,
                    EventType: "delete_project_content",
                    AggregateType: "project",
                    AggregateId: project.Id,
                    PayloadJson: "{}"),
                cancellationToken);

            // Delete project (cascade will handle chapters, foreshadows, etc. via EF Core configuration)
            _context.NovelProjects.Remove(project);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            // Invalidate all per-user/admin views for this project.
            _cache.RemoveByPrefix(BuildProjectCachePrefix(projectId));

            _logger.LogInformation("Deleted project {ProjectId} by user {UserId} and invalidated cache", projectId, userId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Map NovelProject entity to ProjectResponse DTO.
    /// </summary>
    private static ProjectResponse MapToResponse(NovelProject project)
    {
        return new ProjectResponse
        {
            Id = project.Id,
            UserId = project.UserId,
            Title = project.Title,
            Genre = project.Genre,
            SubGenre = project.SubGenre,
            CoreHook = project.CoreHook,
            Status = project.Status,
            WordCount = project.WordCount,
            CoverImageUrl = project.CoverImageUrl,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }

    private static string BuildProjectCacheKey(string projectId, string userId, bool isAdmin) =>
        isAdmin
            ? $"{BuildProjectCachePrefix(projectId)}:admin"
            : $"{BuildProjectCachePrefix(projectId)}:user:{userId}";

    private static string BuildProjectCachePrefix(string projectId) =>
        $"project:{projectId}";

    private async Task<NovelProject?> FindProjectByIdempotencyKeyAsync(
        string userId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await _context.NovelProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(project =>
                    project.UserId == userId &&
                    project.IdempotencyKey == idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

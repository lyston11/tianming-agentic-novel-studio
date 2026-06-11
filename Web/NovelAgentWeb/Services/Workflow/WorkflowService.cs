using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Workflow;

/// <summary>
/// Service for managing volume arc workflow and planning.
/// </summary>
public class WorkflowService : IWorkflowService
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        ILogger<WorkflowService> logger)
    {
        _db = db;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<VolumeArcResponse> CreateVolumeArcAsync(
        CreateVolumeArcRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {request.ProjectId} not found");

        var volumeArc = new VolumeArc
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = request.ProjectId,
            VolumeNumber = request.VolumeNumber,
            VolumeTitle = request.VolumeTitle,
            VolumeTheme = request.VolumeTheme,
            TargetChapters = request.TargetChapters,
            CurrentChapters = 0,
            Act1Setup = request.Act1Setup,
            Act2Confrontation = request.Act2Confrontation,
            Act3Climax = request.Act3Climax,
            Act4Resolution = request.Act4Resolution,
            KeyEvents = request.KeyEvents,
            MajorConflict = request.MajorConflict,
            ConflictEscalation = request.ConflictEscalation,
            Status = "planned",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.VolumeArcs.Add(volumeArc);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created volume arc {VolumeArcId} for project {ProjectId}", volumeArc.Id, request.ProjectId);

        return MapToResponse(volumeArc);
    }

    public async Task<List<VolumeArcResponse>> ListVolumeArcsAsync(string projectId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        var volumeArcs = await _db.VolumeArcs
            .Where(v => v.ProjectId == projectId)
            .OrderBy(v => v.VolumeNumber)
            .ToListAsync(ct);

        return volumeArcs.Select(MapToResponse).ToList();
    }

    public async Task<VolumeArcResponse> GetVolumeArcAsync(string volumeArcId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var volumeArc = await _db.VolumeArcs
            .Include(v => v.Project)
            .FirstOrDefaultAsync(v => v.Id == volumeArcId, ct);

        if (volumeArc == null)
            throw new KeyNotFoundException($"Volume arc {volumeArcId} not found");

        if (volumeArc.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return MapToResponse(volumeArc);
    }

    public async Task<VolumeArcResponse> UpdateVolumeArcAsync(
        string volumeArcId, UpdateVolumeArcRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var volumeArc = await _db.VolumeArcs
            .Include(v => v.Project)
            .FirstOrDefaultAsync(v => v.Id == volumeArcId, ct);

        if (volumeArc == null)
            throw new KeyNotFoundException($"Volume arc {volumeArcId} not found");

        if (volumeArc.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        if (request.VolumeTitle != null)
            volumeArc.VolumeTitle = request.VolumeTitle;
        if (request.VolumeTheme != null)
            volumeArc.VolumeTheme = request.VolumeTheme;
        if (request.TargetChapters.HasValue)
            volumeArc.TargetChapters = request.TargetChapters;
        if (request.CurrentChapters.HasValue)
            volumeArc.CurrentChapters = request.CurrentChapters.Value;
        if (request.Act1Setup != null)
            volumeArc.Act1Setup = request.Act1Setup;
        if (request.Act2Confrontation != null)
            volumeArc.Act2Confrontation = request.Act2Confrontation;
        if (request.Act3Climax != null)
            volumeArc.Act3Climax = request.Act3Climax;
        if (request.Act4Resolution != null)
            volumeArc.Act4Resolution = request.Act4Resolution;
        if (request.KeyEvents != null)
            volumeArc.KeyEvents = request.KeyEvents;
        if (request.MajorConflict != null)
            volumeArc.MajorConflict = request.MajorConflict;
        if (request.ConflictEscalation != null)
            volumeArc.ConflictEscalation = request.ConflictEscalation;
        if (request.Status != null)
        {
            volumeArc.Status = request.Status;
            if (request.Status == "completed" && volumeArc.CompletedAt == null)
                volumeArc.CompletedAt = DateTime.UtcNow;
        }

        volumeArc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated volume arc {VolumeArcId}", volumeArcId);

        return MapToResponse(volumeArc);
    }

    public async Task DeleteVolumeArcAsync(string volumeArcId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var volumeArc = await _db.VolumeArcs
            .Include(v => v.Project)
            .FirstOrDefaultAsync(v => v.Id == volumeArcId, ct);

        if (volumeArc == null)
            throw new KeyNotFoundException($"Volume arc {volumeArcId} not found");

        if (volumeArc.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        _db.VolumeArcs.Remove(volumeArc);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted volume arc {VolumeArcId}", volumeArcId);
    }

    public async Task<WorkspaceResponse> GetWorkspaceAsync(string userId, CancellationToken ct = default)
    {
        var projects = await _db.NovelProjects
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (projects.Count == 0)
        {
            return new WorkspaceResponse
            {
                Projects = new List<WorkspaceProjectView>(),
                TotalCount = 0
            };
        }

        var projectIds = projects.Select(p => p.Id).ToList();

        var volumeCounts = await _db.VolumeArcs
            .Where(v => projectIds.Contains(v.ProjectId))
            .GroupBy(v => v.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);

        var chapterStats = await _db.Chapters
            .Where(c => projectIds.Contains(c.ProjectId))
            .GroupBy(c => c.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                GeneratedCount = g.Count(c => c.Status == "committed"),
                PlannedCount = g.Count(c => c.Status != "committed"),
                NeedsRewriteCount = 0
            })
            .ToDictionaryAsync(x => x.ProjectId, ct);

        var constitutions = await _db.StoryConstitutions
            .Where(sc => projectIds.Contains(sc.ProjectId))
            .ToDictionaryAsync(sc => sc.ProjectId, ct);

        var bookViews = projects.Select(p =>
        {
            var stats = chapterStats.GetValueOrDefault(p.Id);
            var constitution = constitutions.GetValueOrDefault(p.Id);

            return new WorkspaceProjectView
            {
                ProjectId = p.Id,
                Title = p.Title,
                Genre = p.Genre ?? string.Empty,
                SubGenre = p.SubGenre ?? string.Empty,
                CoreHook = constitution?.CoreHook ?? string.Empty,
                ReaderPromise = constitution?.ReaderPromise ?? string.Empty,
                Status = p.Status,
                IsActive = p.Status != "archived",
                VolumeCount = volumeCounts.GetValueOrDefault(p.Id, 0),
                GeneratedChapterCount = stats?.GeneratedCount ?? 0,
                PlannedChapterCount = stats?.PlannedCount ?? 0,
                NeedsRewriteCount = stats?.NeedsRewriteCount ?? 0,
                UpdatedAt = p.UpdatedAt.ToString("o"),
                SelectedChapter = null
            };
        }).ToList();

        return new WorkspaceResponse
        {
            Projects = bookViews,
            TotalCount = bookViews.Count
        };
    }

    private static VolumeArcResponse MapToResponse(VolumeArc volumeArc)
    {
        return new VolumeArcResponse
        {
            Id = volumeArc.Id,
            UserId = volumeArc.UserId,
            ProjectId = volumeArc.ProjectId,
            VolumeNumber = volumeArc.VolumeNumber,
            VolumeTitle = volumeArc.VolumeTitle,
            VolumeTheme = volumeArc.VolumeTheme,
            TargetChapters = volumeArc.TargetChapters,
            CurrentChapters = volumeArc.CurrentChapters,
            Act1Setup = volumeArc.Act1Setup,
            Act2Confrontation = volumeArc.Act2Confrontation,
            Act3Climax = volumeArc.Act3Climax,
            Act4Resolution = volumeArc.Act4Resolution,
            KeyEvents = volumeArc.KeyEvents,
            MajorConflict = volumeArc.MajorConflict,
            ConflictEscalation = volumeArc.ConflictEscalation,
            Status = volumeArc.Status,
            CreatedAt = volumeArc.CreatedAt,
            UpdatedAt = volumeArc.UpdatedAt,
            CompletedAt = volumeArc.CompletedAt
        };
    }
}

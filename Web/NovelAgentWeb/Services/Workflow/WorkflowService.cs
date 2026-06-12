using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Workflow;

/// <summary>
/// Service for managing volume arc workflow and planning.
/// </summary>
public class WorkflowService : IWorkflowService
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly IWorkspaceFactory _workspaceFactory;
    private readonly AgentSessionManager _sessionManager;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        IWorkspaceFactory workspaceFactory,
        AgentSessionManager sessionManager,
        ILogger<WorkflowService> logger)
    {
        _db = db;
        _currentUserService = currentUserService;
        _workspaceFactory = workspaceFactory;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<ProjectWorkflowDocument> GetProjectWorkflowAsync(string projectId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var project = await _db.NovelProjects
            .Include(p => p.StoryConstitution)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        var workspaceEntry = await _workspaceFactory.AcquireAsync(userId, projectId, ct);
        try
        {
            var catalog = new NovelProjectCatalog(workspaceEntry.Workspace);
            await catalog.UpsertAsync(MapToCatalogProject(project), makeActive: false, ct);

            var workflow = await ProjectWorkflow.BuildAsync(
                workspaceEntry.Workspace,
                catalog,
                _sessionManager,
                projectId,
                ct);

            if (workflow == null)
                throw new KeyNotFoundException($"Project {projectId} not found");

            var dbLibrary = await BuildDatabaseLibraryAsync(project, workflow.Library, ct);
            if (dbLibrary != null)
            {
                workflow = workflow with
                {
                    Project = dbLibrary.ActiveBook,
                    Library = dbLibrary,
                    IsEmptyProject = workflow.ActivityScore <= 0 && dbLibrary.PlannedChapterCount == 0 && dbLibrary.GeneratedChapterCount == 0
                };
            }

            return workflow;
        }
        finally
        {
            _workspaceFactory.Release(userId, workspaceEntry.ProjectId);
        }
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

    private static NovelProjectInfo MapToCatalogProject(NovelProject project)
    {
        return new NovelProjectInfo
        {
            Id = project.Id,
            Title = project.Title,
            Genre = project.Genre ?? project.StoryConstitution?.Genre ?? string.Empty,
            SubGenre = project.SubGenre ?? project.StoryConstitution?.SubGenre ?? string.Empty,
            CoreHook = project.CoreHook ?? project.StoryConstitution?.CoreHook ?? string.Empty,
            ReaderPromise = project.StoryConstitution?.ReaderPromise ?? string.Empty,
            Status = project.Status,
            StorageProjectName = string.IsNullOrWhiteSpace(project.StorageProjectName)
                ? $"AgenticNovelStudio__novel__{project.Id[..Math.Min(project.Id.Length, 8)]}"
                : project.StorageProjectName,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }

    private async Task<NovelLibraryDocument?> BuildDatabaseLibraryAsync(
        NovelProject project,
        NovelLibraryDocument currentLibrary,
        CancellationToken ct)
    {
        var volumeArcs = await _db.VolumeArcs
            .Where(v => v.ProjectId == project.Id && v.UserId == project.UserId)
            .OrderBy(v => v.VolumeNumber)
            .ToListAsync(ct);
        var chapters = await _db.Chapters
            .Where(c => c.ProjectId == project.Id)
            .OrderBy(c => c.ChapterNumber)
            .ToListAsync(ct);

        if (volumeArcs.Count == 0 && chapters.Count == 0)
            return null;

        var volumes = BuildDatabaseVolumes(volumeArcs, chapters).ToList();
        var allChapters = volumes.SelectMany(v => v.Chapters).ToList();
        var generatedCount = allChapters.Count(c => c.HasGeneratedContent || IsCommitted(c.Status));
        var needsRewriteCount = allChapters.Count(c => c.NeedsRewrite);
        var selectedChapter = allChapters
            .OrderByDescending(c => c.HasGeneratedContent)
            .ThenBy(c => c.VolumeId)
            .ThenBy(c => c.BeatIndex == 0 ? int.MaxValue : c.BeatIndex)
            .FirstOrDefault();

        var book = new NovelBookView(
            project.Id,
            project.Title,
            project.Genre ?? project.StoryConstitution?.Genre ?? string.Empty,
            project.SubGenre ?? project.StoryConstitution?.SubGenre ?? string.Empty,
            project.CoreHook ?? project.StoryConstitution?.CoreHook ?? string.Empty,
            project.StoryConstitution?.ReaderPromise ?? string.Empty,
            project.Status,
            currentLibrary.ActiveBook?.IsActive ?? project.Status != "archived",
            volumeArcs.Count,
            generatedCount,
            allChapters.Count,
            needsRewriteCount,
            project.UpdatedAt.ToString("O"),
            selectedChapter);

        var books = currentLibrary.Books
            .Where(b => !string.Equals(b.ProjectId, project.Id, StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { book })
            .OrderByDescending(b => b.IsActive)
            .ThenByDescending(b => b.UpdatedAt)
            .ToList();

        return new NovelLibraryDocument(
            books,
            book,
            volumes,
            selectedChapter,
            generatedCount,
            allChapters.Count,
            needsRewriteCount);
    }

    private static IEnumerable<NovelVolumeView> BuildDatabaseVolumes(
        IReadOnlyList<VolumeArc> volumeArcs,
        IReadOnlyList<Chapter> chapters)
    {
        var assignedChapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var volume in volumeArcs)
        {
            var volumeChapters = chapters
                .Where(c => string.Equals(c.VolumeId, volume.Id, StringComparison.OrdinalIgnoreCase))
                .Select(chapter => MapDatabaseChapter(chapter, volume.Id, volume.VolumeTitle, volume.VolumeNumber))
                .ToList();
            foreach (var chapter in volumeChapters)
                assignedChapterIds.Add(chapter.ChapterId);

            yield return new NovelVolumeView(
                volume.Id,
                volume.VolumeTitle,
                volume.Status,
                volumeChapters.FirstOrDefault()?.ChapterId ?? string.Empty,
                volumeChapters.LastOrDefault()?.ChapterId ?? string.Empty,
                volume.TargetChapters ?? volumeChapters.Count,
                volumeChapters);
        }

        var unassigned = chapters
            .Where(c => !assignedChapterIds.Contains(c.Id))
            .Select(chapter => MapDatabaseChapter(chapter, "database-chapters", "数据库章节", 0))
            .ToList();

        if (unassigned.Count > 0 || volumeArcs.Count == 0)
        {
            yield return new NovelVolumeView(
                "database-chapters",
                "数据库章节",
                "Draft",
                unassigned.FirstOrDefault()?.ChapterId ?? string.Empty,
                unassigned.LastOrDefault()?.ChapterId ?? string.Empty,
                unassigned.Count,
                unassigned);
        }
    }

    private static NovelChapterView MapDatabaseChapter(Chapter chapter, string volumeId, string volumeTitle, int volumeNumber)
    {
        var writingStatus = ResolveChapterWritingStatus(chapter);
        var hasContent = chapter.WordCount > 0 || IsCommitted(chapter.Status);

        return new NovelChapterView(
            chapter.Id,
            chapter.Title,
            volumeId,
            volumeTitle,
            chapter.ChapterNumber,
            volumeNumber > 0 ? $"第 {volumeNumber} 卷" : string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            chapter.Status,
            string.Empty,
            string.Empty,
            chapter.UpdatedAt.ToString("O"),
            hasContent,
            IsRewriteStatus(chapter.Status),
            chapter.WordCount,
            chapter.Title,
            string.Empty,
            string.Empty,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            writingStatus,
            string.Empty,
            IsCommitted(chapter.Status) ? "committed" : string.Empty,
            string.Empty,
            false,
            false,
            false,
            false,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            true,
            IsCommitted(chapter.Status),
            IsCommitted(chapter.Status) ? "已入库" : "数据库章节",
            writingStatus,
            string.Empty,
            string.Empty,
            string.Empty);
    }

    private static string ResolveChapterWritingStatus(Chapter chapter)
    {
        if (IsCommitted(chapter.Status)) return "committed";
        if (IsRewriteStatus(chapter.Status)) return "quality_failed";
        return string.IsNullOrWhiteSpace(chapter.Status) ? "planned" : chapter.Status;
    }

    private static bool IsCommitted(string? status) =>
        string.Equals(status, "committed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "published", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    private static bool IsRewriteStatus(string? status) =>
        string.Equals(status, "needs_rewrite", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "rewrite", StringComparison.OrdinalIgnoreCase);
}

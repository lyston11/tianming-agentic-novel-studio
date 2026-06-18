using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
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
    private readonly MissionBlackboardRecoveryService _blackboardRecovery;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        IWorkspaceFactory workspaceFactory,
        AgentSessionManager sessionManager,
        MissionBlackboardRecoveryService blackboardRecovery,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowService> logger)
    {
        _db = db;
        _currentUserService = currentUserService;
        _workspaceFactory = workspaceFactory;
        _sessionManager = sessionManager;
        _blackboardRecovery = blackboardRecovery;
        _scopeFactory = scopeFactory;
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
            var catalog = new NovelProjectCatalog(workspaceEntry.Workspace, _scopeFactory);
            await catalog.UpsertAsync(MapToCatalogProject(project), makeActive: false, ct);

            var workflow = await ProjectWorkflow.BuildAsync(
                workspaceEntry.Workspace,
                catalog,
                _sessionManager,
                _blackboardRecovery,
                projectId,
                ct);

            if (workflow == null)
                throw new KeyNotFoundException($"Project {projectId} not found");

            var dbLibrary = await BuildDatabaseLibraryAsync(project, workflow.Library, workflow.ChapterArtifacts, ct);
            if (dbLibrary != null)
            {
                var dbTimeline = ProjectWorkflow.BuildArtifactTimeline(
                    dbLibrary,
                    new StoryBibleDocument { AgentRuns = workflow.Runs.ToList() },
                    workflow.ChapterArtifacts,
                    workflow.SchedulerTasks);
                var dbStages = ProjectWorkflow.BuildProductionStages(dbLibrary, dbTimeline, workflow.SchedulerTasks);

                workflow = workflow with
                {
                    Project = dbLibrary.ActiveBook,
                    Library = dbLibrary,
                    IsEmptyProject = workflow.ActivityScore <= 0 && dbLibrary.PlannedChapterCount == 0 && dbLibrary.GeneratedChapterCount == 0,
                    ProductionStages = dbStages,
                    ArtifactTimeline = dbTimeline
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
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }

    private async Task<NovelLibraryDocument?> BuildDatabaseLibraryAsync(
        NovelProject project,
        NovelLibraryDocument currentLibrary,
        IReadOnlyList<WorkflowChapterArtifactSummary> chapterArtifacts,
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

        var chapterContents = await LoadChapterContentsAsync(chapters, ct);
        var volumes = BuildDatabaseVolumes(volumeArcs, chapters, chapterArtifacts, chapterContents).ToList();
        var allChapters = volumes.SelectMany(v => v.Chapters).ToList();
        var generatedCount = allChapters.Count(c => c.HasGeneratedContent || IsCommitted(c.Status));
        var plannedChapterCount = Math.Max(
            allChapters.Count,
            volumeArcs.Sum(v => Math.Max(v.TargetChapters ?? 0, v.CurrentChapters)));
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
            plannedChapterCount,
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
            plannedChapterCount,
            needsRewriteCount);
    }

    private static IEnumerable<NovelVolumeView> BuildDatabaseVolumes(
        IReadOnlyList<VolumeArc> volumeArcs,
        IReadOnlyList<Chapter> chapters,
        IReadOnlyList<WorkflowChapterArtifactSummary> chapterArtifacts,
        IReadOnlyDictionary<string, string> chapterContents)
    {
        var assignedChapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var syntheticChapters = BuildSyntheticWorkflowChapters(chapterArtifacts);
        var assignedSyntheticIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var databaseChapterIds = chapters.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var titleByChapterId = chapterArtifacts
            .Where(a => !string.IsNullOrWhiteSpace(a.ChapterId) && !string.IsNullOrWhiteSpace(a.CandidateTitle))
            .GroupBy(a => a.ChapterId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                ChapterId = group.Key,
                Title = group
                    .OrderByDescending(a => a.UpdatedAt)
                    .Select(a => a.CandidateTitle)
                    .FirstOrDefault(title => !IsWeakChapterTitle(title))
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToDictionary(item => item.ChapterId, item => item.Title!, StringComparer.OrdinalIgnoreCase);
        var nextChapterStart = 1;

        foreach (var volume in volumeArcs)
        {
            var targetCount = Math.Max(volume.TargetChapters ?? 0, volume.CurrentChapters);
            var chapterEnd = targetCount > 0 ? nextChapterStart + targetCount - 1 : int.MaxValue;
            var volumeChapters = chapters
                .Where(c => string.Equals(c.VolumeId, volume.Id, StringComparison.OrdinalIgnoreCase))
                .Select(chapter => MapDatabaseChapter(chapter, volume.Id, volume.VolumeTitle, volume.VolumeNumber, chapterContents, titleByChapterId))
                .ToList();
            if (volumeChapters.Count == 0)
            {
                volumeChapters = chapters
                    .Where(c => !assignedChapterIds.Contains(c.Id))
                    .Where(c => string.IsNullOrWhiteSpace(c.VolumeId))
                    .Where(c => c.ChapterNumber >= nextChapterStart && c.ChapterNumber <= chapterEnd)
                    .Select(chapter => MapDatabaseChapter(chapter, volume.Id, volume.VolumeTitle, volume.VolumeNumber, chapterContents, titleByChapterId))
                    .ToList();
            }
            if (volumeChapters.Count == 0 && volume.VolumeNumber == 1)
            {
                volumeChapters.AddRange(syntheticChapters
                    .Where(artifact => !databaseChapterIds.Contains(artifact.ChapterId))
                    .Select((artifact, index) =>
                    MapWorkflowArtifactChapter(artifact, volume.Id, volume.VolumeTitle, volume.VolumeNumber, index + 1)));
                foreach (var chapter in volumeChapters)
                    assignedSyntheticIds.Add(chapter.ChapterId);
            }
            foreach (var chapter in volumeChapters)
                assignedChapterIds.Add(chapter.ChapterId);
            if (targetCount > 0)
                nextChapterStart = chapterEnd + 1;

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
            .Select(chapter => MapDatabaseChapter(chapter, "database-chapters", "数据库章节", 0, chapterContents, titleByChapterId))
            .ToList();
        unassigned.AddRange(syntheticChapters
            .Where(a => !assignedSyntheticIds.Contains(a.ChapterId))
            .Where(a => !databaseChapterIds.Contains(a.ChapterId))
            .Select((artifact, index) => MapWorkflowArtifactChapter(artifact, "workflow-artifacts", "工作流章节", 0, index + 1)));

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

    private static List<WorkflowChapterArtifactSummary> BuildSyntheticWorkflowChapters(
        IReadOnlyList<WorkflowChapterArtifactSummary> chapterArtifacts)
    {
        return chapterArtifacts
            .Where(a => !string.IsNullOrWhiteSpace(a.ChapterId) && (a.HasDraft || a.GateStatus.Length > 0 || a.DraftStatus.Length > 0))
            .GroupBy(a => a.ChapterId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(a => a.HasDraft)
                .ThenByDescending(a => string.Equals(a.GateStatus, "validated", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(a => a.UpdatedAt)
                .First())
            .OrderBy(a => ExtractChapterNumber(a.ChapterId))
            .ToList();
    }

    private static NovelChapterView MapWorkflowArtifactChapter(
        WorkflowChapterArtifactSummary artifact,
        string volumeId,
        string volumeTitle,
        int volumeNumber,
        int fallbackIndex)
    {
        var beatIndex = ExtractChapterNumber(artifact.ChapterId);
        if (beatIndex <= 0)
            beatIndex = fallbackIndex;
        var status = string.IsNullOrWhiteSpace(artifact.Status) ? artifact.DraftStatus : artifact.Status;
        var writingStatus = ResolveArtifactWritingStatus(artifact);
        var title = string.IsNullOrWhiteSpace(artifact.CandidateTitle)
            ? $"第 {beatIndex} 章"
            : artifact.CandidateTitle;

        return new NovelChapterView(
            artifact.ChapterId,
            title,
            volumeId,
            volumeTitle,
            beatIndex,
            volumeNumber > 0 ? $"第 {volumeNumber} 卷" : string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            status,
            artifact.RunId,
            artifact.Intent,
            artifact.UpdatedAt,
            artifact.HasDraft,
            string.Equals(artifact.QualityStatus, "quality_failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(artifact.Status, "blocked", StringComparison.OrdinalIgnoreCase),
            0,
            artifact.CandidateTitle,
            artifact.DraftPreview,
            artifact.CandidateTitle,
            artifact.QualityScore,
            0,
            artifact.QualityIssues,
            Array.Empty<string>(),
            writingStatus,
            string.Join("; ", artifact.ContextWarnings),
            artifact.DraftStatus,
            artifact.GateStatus,
            string.Equals(artifact.GateStatus, "validated", StringComparison.OrdinalIgnoreCase),
            string.Equals(artifact.GateStatus, "validated", StringComparison.OrdinalIgnoreCase),
            string.Equals(artifact.GateStatus, "validated", StringComparison.OrdinalIgnoreCase),
            artifact.RagRecallCount > 0,
            artifact.RagRecallCount,
            0,
            artifact.GateIssues,
            artifact.RepairHints,
            artifact.DependencyWarnings,
            artifact.ContextWarnings,
            true,
            false,
            ArtifactUserVisibleStatus(artifact),
            writingStatus,
            artifact.RunId,
            artifact.GateStatus,
            artifact.QualityStatus);
    }

    private static string ResolveArtifactWritingStatus(WorkflowChapterArtifactSummary artifact)
    {
        if (string.Equals(artifact.GateStatus, "validated", StringComparison.OrdinalIgnoreCase))
            return "validated";
        if (artifact.HasDraft)
            return "draft_generated";
        if (!string.IsNullOrWhiteSpace(artifact.DraftStatus))
            return artifact.DraftStatus;
        return "planned";
    }

    private static string ArtifactUserVisibleStatus(WorkflowChapterArtifactSummary artifact)
    {
        if (string.Equals(artifact.GateStatus, "validated", StringComparison.OrdinalIgnoreCase))
            return "硬门禁已通过";
        if (artifact.HasDraft)
            return "草稿在工作流中";
        return "工作流章节";
    }

    private static int ExtractChapterNumber(string chapterId)
    {
        var digits = new string((chapterId ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    private async Task<Dictionary<string, string>> LoadChapterContentsAsync(
        IReadOnlyList<Chapter> chapters,
        CancellationToken ct)
    {
        var documentIds = chapters
            .Select(c => c.CurrentDocumentId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (documentIds.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var chunks = await _db.ContentChunks
            .AsNoTracking()
            .Where(chunk => documentIds.Contains(chunk.DocumentId))
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new { chunk.DocumentId, chunk.ChunkText })
            .ToListAsync(ct);

        var contentByDocumentId = chunks
            .GroupBy(chunk => chunk.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Join(string.Empty, group.Select(chunk => chunk.ChunkText)),
                StringComparer.OrdinalIgnoreCase);

        return chapters
            .Where(chapter => !string.IsNullOrWhiteSpace(chapter.CurrentDocumentId))
            .Where(chapter => contentByDocumentId.ContainsKey(chapter.CurrentDocumentId!))
            .ToDictionary(
                chapter => chapter.Id,
                chapter => contentByDocumentId[chapter.CurrentDocumentId!],
                StringComparer.OrdinalIgnoreCase);
    }

    private static NovelChapterView MapDatabaseChapter(
        Chapter chapter,
        string volumeId,
        string volumeTitle,
        int volumeNumber,
        IReadOnlyDictionary<string, string> chapterContents,
        IReadOnlyDictionary<string, string> titleByChapterId)
    {
        var writingStatus = ResolveChapterWritingStatus(chapter);
        var content = chapterContents.TryGetValue(chapter.Id, out var value) ? value : string.Empty;
        var wordCount = chapter.WordCount > 0 ? chapter.WordCount : CountWords(content);
        var hasContent = wordCount > 0 || !string.IsNullOrWhiteSpace(content) || IsCommitted(chapter.Status);
        var committed = IsCommitted(chapter.Status);
        titleByChapterId.TryGetValue(chapter.Id, out var artifactTitle);
        var displayTitle = ResolveReadableChapterTitle(chapter.Title, content, chapter.ChapterNumber, artifactTitle);

        return new NovelChapterView(
            chapter.Id,
            displayTitle,
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
            wordCount,
            displayTitle,
            content,
            string.Empty,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            writingStatus,
            string.Empty,
            committed ? "committed" : string.Empty,
            committed ? "validated" : string.Empty,
            committed,
            committed,
            committed,
            false,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            true,
            committed,
            committed ? "已入库" : "数据库章节",
            writingStatus,
            string.Empty,
            committed ? $"gate:{chapter.Id}" : string.Empty,
            string.Empty);
    }

    private static string ResolveReadableChapterTitle(
        string storedTitle,
        string content,
        int chapterNumber,
        string? artifactTitle = null)
    {
        var heading = ExtractChapterHeading(content);
        if (!string.IsNullOrWhiteSpace(heading))
            return heading;

        if (!IsMachineChapterTitle(storedTitle))
            return storedTitle;

        if (!IsWeakChapterTitle(artifactTitle))
            return artifactTitle!.Trim();

        return chapterNumber > 0 ? $"第 {chapterNumber} 章" : storedTitle;
    }

    private static bool IsMachineChapterTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return true;

        var normalized = title.Trim();
        if (!normalized.StartsWith("chapter-", StringComparison.OrdinalIgnoreCase))
            return false;

        return normalized.Skip("chapter-".Length).All(c => char.IsDigit(c) || c == '_' || c == '-');
    }

    private static bool IsWeakChapterTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return true;

        var normalized = title.Trim();
        return normalized.Equals("目标推进", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("章节推进", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("正文已经提交到书城", StringComparison.OrdinalIgnoreCase)
            || IsMachineChapterTitle(normalized);
    }

    private static string ExtractChapterHeading(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var lineEnd = content.IndexOfAny(new[] { '\r', '\n' });
        var firstLine = (lineEnd >= 0 ? content[..lineEnd] : content).Trim();
        if (firstLine.StartsWith("#", StringComparison.Ordinal))
            firstLine = firstLine.TrimStart('#').Trim();
        if (firstLine.Length == 0)
            return string.Empty;

        var chapterStart = firstLine.IndexOf('第');
        var chapterEnd = firstLine.IndexOf('章', chapterStart >= 0 ? chapterStart : 0);
        if (chapterStart < 0 || chapterEnd <= chapterStart)
            return firstLine.Length <= 24 ? firstLine : string.Empty;

        var prefix = firstLine[chapterStart..(chapterEnd + 1)].Trim();
        var rest = firstLine[(chapterEnd + 1)..].TrimStart(' ', '\t', ':', '：', '-', '—');
        if (string.IsNullOrWhiteSpace(rest))
            return prefix;

        var subtitle = TakeCompactSubtitle(rest);
        return string.IsNullOrWhiteSpace(subtitle) ? prefix : $"{prefix}：{subtitle}";
    }

    private static string TakeCompactSubtitle(string text)
    {
        var chars = new List<char>(capacity: 8);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || "，,。.!！?？；;：:、\"“”'‘’（）()【】[]".Contains(c))
                break;
            chars.Add(c);
            if (chars.Count >= 6)
                break;
        }

        if (chars.Count >= 6 && chars[^1] == chars[0])
            chars.RemoveAt(chars.Count - 1);

        return new string(chars.ToArray()).Trim();
    }

    private static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        var count = 0;
        var inWord = false;
        foreach (var c in content)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (c is >= '\u4E00' and <= '\u9FFF')
            {
                count++;
                inWord = false;
            }
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
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

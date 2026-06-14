using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using DbNovelProject = TM.Web.NovelAgentWeb.Data.Entities.NovelProject;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class NovelProjectCatalog
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly object _memoryLock = new();
    private readonly NovelProjectCatalogDocument _memoryDocument = new();

    public NovelProjectCatalog(NovelAgentWorkspace workspace, IServiceScopeFactory? scopeFactory = null)
    {
        _workspace = workspace;
        _scopeFactory = scopeFactory;
    }

    public async Task<NovelProjectCatalogDocument> GetAsync(CancellationToken ct = default)
    {
        if (_scopeFactory == null)
            return GetMemoryDocument();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var projects = await db.NovelProjects
            .AsNoTracking()
            .Where(p => p.UserId == _workspace.UserId)
            .OrderByDescending(p => p.UpdatedAt)
            .ThenBy(p => p.Title)
            .Select(p => MapToInfo(p))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new NovelProjectCatalogDocument
        {
            ActiveProjectId = ResolveActiveProjectId(projects),
            Projects = projects,
            UpdatedAt = projects.Count == 0 ? DateTime.UtcNow : projects.Max(p => p.UpdatedAt)
        };
    }

    public async Task<NovelProjectInfo> GetActiveAsync(CancellationToken ct = default)
    {
        var document = await GetAsync(ct).ConfigureAwait(false);
        if (document.Projects.Count == 0)
            throw new InvalidOperationException("未找到小说项目，请先创建新小说。");

        var active = document.Projects.FirstOrDefault(p =>
            string.Equals(p.Id, document.ActiveProjectId, StringComparison.OrdinalIgnoreCase));
        return active ?? document.Projects[0];
    }

    public async Task<NovelProjectInfo?> FindAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        if (_scopeFactory == null)
        {
            var document = GetMemoryDocument();
            return document.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var project = await db.NovelProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == _workspace.UserId, ct)
            .ConfigureAwait(false);
        return project == null ? null : MapToInfo(project);
    }

    public async Task<NovelProjectInfo> UpsertAsync(
        NovelProjectInfo source,
        bool makeActive = false,
        CancellationToken ct = default)
    {
        if (_scopeFactory == null)
            return UpsertMemory(source, makeActive);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var project = await db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == source.Id && p.UserId == _workspace.UserId, ct)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;
        if (project == null)
        {
            project = new DbNovelProject
            {
                Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id,
                UserId = _workspace.UserId,
                CreatedAt = source.CreatedAt == default ? now : source.CreatedAt
            };
            db.NovelProjects.Add(project);
        }

        ApplyInfo(project, source, now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MapToInfo(project);
    }

    public async Task<NovelProjectInfo> ActivateAsync(string projectId, CancellationToken ct = default)
    {
        var project = await FindAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"小说项目不存在：{projectId}");

        if (_scopeFactory == null)
        {
            lock (_memoryLock)
            {
                _memoryDocument.ActiveProjectId = project.Id;
                _memoryDocument.UpdatedAt = DateTime.UtcNow;
            }
        }

        return project;
    }

    public async Task<NovelProjectDeleteResult> DeleteAsync(string projectId, CancellationToken ct = default)
    {
        if (_scopeFactory == null)
            return DeleteMemory(projectId);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var projects = await db.NovelProjects
            .Where(p => p.UserId == _workspace.UserId)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var project = projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (project == null)
            return new NovelProjectDeleteResult(false, "小说项目不存在。", ResolveActiveProjectId(projects.Select(MapToInfo).ToList()));
        if (projects.Count <= 1)
            return new NovelProjectDeleteResult(false, "至少需要保留一本小说。", project.Id);

        db.NovelProjects.Remove(project);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var remaining = projects
            .Where(p => !string.Equals(p.Id, project.Id, StringComparison.OrdinalIgnoreCase))
            .Select(MapToInfo)
            .ToList();
        return new NovelProjectDeleteResult(true, $"已从书架移除「{project.Title}」。", ResolveActiveProjectId(remaining));
    }

    public async Task<NovelProjectInfo?> UpdateAsync(string projectId, NovelProjectUpdateRequest request, CancellationToken ct = default)
    {
        if (_scopeFactory == null)
            return UpdateMemory(projectId, request);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var project = await db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == _workspace.UserId, ct)
            .ConfigureAwait(false);
        if (project == null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.Title))
            project.Title = request.Title.Trim();
        if (!string.IsNullOrWhiteSpace(request.Status))
            project.Status = request.Status.Trim();
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MapToInfo(project);
    }

    public async Task<NovelProjectInfo> CreateAsync(NovelProjectCreateRequest request, CancellationToken ct = default)
    {
        var title = FirstNonEmpty(request.Title, GuessTitle(request.Seed), "未命名新书");
        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var project = new NovelProjectInfo
        {
            Id = id,
            Title = title,
            Genre = request.Genre ?? string.Empty,
            CoreHook = request.Seed ?? string.Empty,
            Status = "Drafting",
            CreatedAt = now,
            UpdatedAt = now,
        };

        return await UpsertAsync(project, makeActive: true, ct).ConfigureAwait(false);
    }

    public async Task<NovelProjectInfo?> UpdateFromCurrentStoryBibleAsync(string? projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        var project = await FindAsync(projectId, ct).ConfigureAwait(false);
        if (project == null)
            return null;

        var bible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
        ApplyBibleMetadata(project, bible);
        project.UpdatedAt = DateTime.UtcNow;
        return await UpsertAsync(project, makeActive: false, ct).ConfigureAwait(false);
    }

    public async Task<T> WithProjectAsync<T>(NovelProjectInfo project, Func<Task<T>> operation, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await operation().ConfigureAwait(false);
    }

    private NovelProjectCatalogDocument GetMemoryDocument()
    {
        lock (_memoryLock)
        {
            return CloneDocument(_memoryDocument);
        }
    }

    private NovelProjectInfo UpsertMemory(NovelProjectInfo source, bool makeActive)
    {
        lock (_memoryLock)
        {
            var now = DateTime.UtcNow;
            var project = _memoryDocument.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, source.Id, StringComparison.OrdinalIgnoreCase));
            if (project == null)
            {
                project = new NovelProjectInfo { Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id };
                _memoryDocument.Projects.Insert(0, project);
            }

            CopyInfo(project, source, now);
            if (makeActive || string.IsNullOrWhiteSpace(_memoryDocument.ActiveProjectId))
                _memoryDocument.ActiveProjectId = project.Id;
            _memoryDocument.UpdatedAt = now;
            return CloneInfo(project);
        }
    }

    private NovelProjectDeleteResult DeleteMemory(string projectId)
    {
        lock (_memoryLock)
        {
            var project = _memoryDocument.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
            if (project == null)
                return new NovelProjectDeleteResult(false, "小说项目不存在。", _memoryDocument.ActiveProjectId);
            if (_memoryDocument.Projects.Count <= 1)
                return new NovelProjectDeleteResult(false, "至少需要保留一本小说。", _memoryDocument.ActiveProjectId);

            _memoryDocument.Projects.Remove(project);
            if (string.Equals(_memoryDocument.ActiveProjectId, project.Id, StringComparison.OrdinalIgnoreCase))
                _memoryDocument.ActiveProjectId = _memoryDocument.Projects[0].Id;
            _memoryDocument.UpdatedAt = DateTime.UtcNow;
            return new NovelProjectDeleteResult(true, $"已从书架移除「{project.Title}」。", _memoryDocument.ActiveProjectId);
        }
    }

    private NovelProjectInfo? UpdateMemory(string projectId, NovelProjectUpdateRequest request)
    {
        lock (_memoryLock)
        {
            var project = _memoryDocument.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
            if (project == null)
                return null;
            if (!string.IsNullOrWhiteSpace(request.Title))
                project.Title = request.Title.Trim();
            if (!string.IsNullOrWhiteSpace(request.Status))
                project.Status = request.Status.Trim();
            project.UpdatedAt = DateTime.UtcNow;
            _memoryDocument.UpdatedAt = project.UpdatedAt;
            return CloneInfo(project);
        }
    }

    private string ResolveActiveProjectId(IReadOnlyList<NovelProjectInfo> projects)
    {
        if (projects.Count == 0)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(_workspace.ProjectId) &&
            !_workspace.ProjectId.StartsWith("temp-", StringComparison.OrdinalIgnoreCase) &&
            projects.Any(p => string.Equals(p.Id, _workspace.ProjectId, StringComparison.OrdinalIgnoreCase)))
        {
            return _workspace.ProjectId;
        }

        return projects[0].Id;
    }

    private static void ApplyInfo(DbNovelProject target, NovelProjectInfo source, DateTime now)
    {
        target.Title = FirstNonEmpty(source.Title, target.Title, "未命名小说");
        target.Genre = EmptyToNull(FirstNonEmpty(source.Genre, target.Genre));
        target.SubGenre = EmptyToNull(FirstNonEmpty(source.SubGenre, target.SubGenre));
        target.CoreHook = EmptyToNull(FirstNonEmpty(source.CoreHook, target.CoreHook));
        target.Status = FirstNonEmpty(source.Status, target.Status, "Drafting");
        target.UpdatedAt = source.UpdatedAt == default ? now : source.UpdatedAt;
    }

    private static NovelProjectInfo MapToInfo(DbNovelProject project) => new()
    {
        Id = project.Id,
        Title = project.Title,
        Genre = project.Genre ?? string.Empty,
        SubGenre = project.SubGenre ?? string.Empty,
        CoreHook = project.CoreHook ?? string.Empty,
        ReaderPromise = string.Empty,
        Status = project.Status,
        CreatedAt = project.CreatedAt,
        UpdatedAt = project.UpdatedAt
    };

    private static NovelProjectCatalogDocument CloneDocument(NovelProjectCatalogDocument source) => new()
    {
        ActiveProjectId = source.ActiveProjectId,
        Projects = source.Projects.Select(CloneInfo).ToList(),
        UpdatedAt = source.UpdatedAt
    };

    private static NovelProjectInfo CloneInfo(NovelProjectInfo source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Genre = source.Genre,
        SubGenre = source.SubGenre,
        CoreHook = source.CoreHook,
        ReaderPromise = source.ReaderPromise,
        Status = source.Status,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    private static void CopyInfo(NovelProjectInfo target, NovelProjectInfo source, DateTime now)
    {
        target.Title = FirstNonEmpty(source.Title, target.Title, "未命名小说");
        target.Genre = FirstNonEmpty(source.Genre, target.Genre);
        target.SubGenre = FirstNonEmpty(source.SubGenre, target.SubGenre);
        target.CoreHook = FirstNonEmpty(source.CoreHook, target.CoreHook);
        target.ReaderPromise = FirstNonEmpty(source.ReaderPromise, target.ReaderPromise);
        target.Status = FirstNonEmpty(source.Status, target.Status, "Drafting");
        target.CreatedAt = source.CreatedAt == default ? target.CreatedAt : source.CreatedAt;
        target.UpdatedAt = source.UpdatedAt == default ? now : source.UpdatedAt;
    }

    private static void ApplyBibleMetadata(NovelProjectInfo project, StoryBibleDocument bible)
    {
        var constitution = bible.Constitution;
        if (constitution != null)
        {
            project.Title = FirstNonEmpty(project.Title == "当前小说" ? string.Empty : project.Title, GuessTitle(constitution.CoreHook), bible.VolumeArcs.FirstOrDefault()?.Title, "未命名小说");
            project.Genre = FirstNonEmpty(constitution.Genre, project.Genre);
            project.SubGenre = FirstNonEmpty(constitution.SubGenre, project.SubGenre);
            project.CoreHook = FirstNonEmpty(constitution.CoreHook, project.CoreHook);
            project.ReaderPromise = FirstNonEmpty(constitution.ReaderPromise, project.ReaderPromise);
        }
        else if (bible.MacroCandidates.Count > 0)
        {
            var candidate = bible.MacroCandidates[0];
            project.Title = FirstNonEmpty(project.Title == "当前小说" ? string.Empty : project.Title, candidate.Title, "未命名小说");
            project.CoreHook = FirstNonEmpty(candidate.CoreHook, project.CoreHook);
        }

        var generated = bible.AgentRuns.Count(r => r.Notes.Any(n => n.Contains("章节生成完成", StringComparison.OrdinalIgnoreCase)));
        project.Status = generated > 0 ? "Writing" : (constitution == null ? "Foundation" : "Planning");
    }

    private static string GuessTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value.Trim();
        var quoted = Regex.Match(text, "[《「\"](?<title>[^》」\"]{2,30})[》」\"]");
        if (quoted.Success)
            return quoted.Groups["title"].Value.Trim();

        var titleMatch = Regex.Match(text, "(?:书名|标题|叫|名叫)[:：\\s]*(?<title>[\\u4e00-\\u9fa5A-Za-z0-9_-]{2,24})");
        if (titleMatch.Success)
            return titleMatch.Groups["title"].Value.Trim();

        return text.Length <= 18 ? text : string.Empty;
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

public sealed class NovelProjectCatalogDocument
{
    public string ActiveProjectId { get; set; } = string.Empty;
    public List<NovelProjectInfo> Projects { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class NovelProjectInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "未命名小说";
    public string Genre { get; set; } = string.Empty;
    public string SubGenre { get; set; } = string.Empty;
    public string CoreHook { get; set; } = string.Empty;
    public string ReaderPromise { get; set; } = string.Empty;
    public string Status { get; set; } = "Drafting";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record NovelProjectCreateRequest(
    string? Title,
    string? Genre,
    string? Seed);

public sealed record NovelProjectUpdateRequest(
    string? Title,
    string? Status);

public sealed record NovelProjectDeleteResult(
    bool Success,
    string Message,
    string ActiveProjectId);

using System.Text.Json;
using System.Text.RegularExpressions;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class NovelProjectCatalog
{
    private const string DefaultProjectId = "default";
    private readonly NovelAgentWorkspace _workspace;
    private readonly object _fileLock = new();
    private readonly string _catalogPath;

    public NovelProjectCatalog(NovelAgentWorkspace workspace)
    {
        _workspace = workspace;
        var dir = Path.Combine(workspace.StorageRoot, "Projects", workspace.ProjectName, "NovelProjects");
        Directory.CreateDirectory(dir);
        _catalogPath = Path.Combine(dir, "projects.json");
    }

    public async Task<NovelProjectCatalogDocument> GetAsync(CancellationToken ct = default)
    {
        var document = LoadDocument();
        if (document.Projects.Count == 0)
        {
            var current = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
            document.Projects.Add(BuildDefaultProject(current));
            document.ActiveProjectId = DefaultProjectId;
            SaveDocument(document);
        }

        if (string.IsNullOrWhiteSpace(document.ActiveProjectId) ||
            document.Projects.All(p => !string.Equals(p.Id, document.ActiveProjectId, StringComparison.OrdinalIgnoreCase)))
        {
            document.ActiveProjectId = document.Projects[0].Id;
            SaveDocument(document);
        }

        return document;
    }

    public async Task<NovelProjectInfo> GetActiveAsync(CancellationToken ct = default)
    {
        var document = await GetAsync(ct).ConfigureAwait(false);
        return document.Projects.First(p => string.Equals(p.Id, document.ActiveProjectId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<NovelProjectInfo?> FindAsync(string projectId, CancellationToken ct = default)
    {
        var document = await GetAsync(ct).ConfigureAwait(false);
        return document.Projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<NovelProjectInfo> ActivateAsync(string projectId, CancellationToken ct = default)
    {
        var document = await GetAsync(ct).ConfigureAwait(false);
        var project = document.Projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"小说项目不存在：{projectId}");

        document.ActiveProjectId = project.Id;
        project.UpdatedAt = DateTime.UtcNow;
        SaveDocument(document);

        await _workspace.ProjectContextLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            StoragePathHelper.CurrentProjectName = project.StorageProjectName;
        }
        finally
        {
            _workspace.ProjectContextLock.Release();
        }

        return project;
    }

    public async Task<NovelProjectDeleteResult> DeleteAsync(string projectId, CancellationToken ct = default)
    {
        var document = await GetAsync(ct).ConfigureAwait(false);
        var project = document.Projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (project == null)
            return new NovelProjectDeleteResult(false, "小说项目不存在。", document.ActiveProjectId);

        if (document.Projects.Count <= 1)
            return new NovelProjectDeleteResult(false, "至少需要保留一本小说。", document.ActiveProjectId);

        document.Projects.Remove(project);
        if (string.Equals(document.ActiveProjectId, project.Id, StringComparison.OrdinalIgnoreCase))
            document.ActiveProjectId = document.Projects[0].Id;

        SaveDocument(document);
        var active = document.Projects.First(p => string.Equals(p.Id, document.ActiveProjectId, StringComparison.OrdinalIgnoreCase));
        await ActivateAsync(active.Id, ct).ConfigureAwait(false);

        return new NovelProjectDeleteResult(true, $"已从书架移除「{project.Title}」。原始工程文件仍保留在本地存储中。", document.ActiveProjectId);
    }

    public async Task<NovelProjectInfo?> UpdateAsync(string projectId, NovelProjectUpdateRequest request, CancellationToken ct = default)
    {
        var document = await GetAsync(ct).ConfigureAwait(false);
        var project = document.Projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (project == null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.Title))
            project.Title = request.Title.Trim();
        if (!string.IsNullOrWhiteSpace(request.Status))
            project.Status = request.Status.Trim();

        project.UpdatedAt = DateTime.UtcNow;
        SaveDocument(document);
        return project;
    }

    public async Task<NovelProjectInfo> CreateAsync(NovelProjectCreateRequest request, CancellationToken ct = default)
    {
        var document = await GetAsync(ct).ConfigureAwait(false);
        var title = FirstNonEmpty(request.Title, GuessTitle(request.Seed), "未命名新书");
        var id = Guid.NewGuid().ToString("N");
        var project = new NovelProjectInfo
        {
            Id = id,
            Title = title,
            Genre = request.Genre ?? string.Empty,
            CoreHook = request.Seed ?? string.Empty,
            Status = "Drafting",
            StorageProjectName = $"{_workspace.ProjectName}__novel__{Slugify(title)}__{id[..8]}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        document.Projects.Insert(0, project);
        document.ActiveProjectId = project.Id;
        SaveDocument(document);

        await _workspace.ProjectContextLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            StoragePathHelper.CurrentProjectName = project.StorageProjectName;
            Directory.CreateDirectory(Path.Combine(_workspace.StorageRoot, "Projects", project.StorageProjectName));
        }
        finally
        {
            _workspace.ProjectContextLock.Release();
        }

        return project;
    }

    public async Task<NovelProjectInfo?> UpdateFromCurrentStoryBibleAsync(string? projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        var document = await GetAsync(ct).ConfigureAwait(false);
        var project = document.Projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (project == null)
            return null;

        var bible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
        ApplyBibleMetadata(project, bible);
        project.UpdatedAt = DateTime.UtcNow;
        SaveDocument(document);
        return project;
    }

    public async Task<T> WithProjectAsync<T>(NovelProjectInfo project, Func<Task<T>> operation, CancellationToken ct = default)
    {
        await _workspace.ProjectContextLock.WaitAsync(ct).ConfigureAwait(false);
        var previous = StoragePathHelper.CurrentProjectName;
        try
        {
            StoragePathHelper.CurrentProjectName = project.StorageProjectName;
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            StoragePathHelper.CurrentProjectName = previous;
            _workspace.ProjectContextLock.Release();
        }
    }

    public NovelProjectCatalogDocument LoadDocument()
    {
        lock (_fileLock)
        {
            if (!File.Exists(_catalogPath))
                return new NovelProjectCatalogDocument();

            try
            {
                var json = File.ReadAllText(_catalogPath);
                return JsonSerializer.Deserialize<NovelProjectCatalogDocument>(json, JsonOptions()) ?? new NovelProjectCatalogDocument();
            }
            catch
            {
                return new NovelProjectCatalogDocument();
            }
        }
    }

    private void SaveDocument(NovelProjectCatalogDocument document)
    {
        lock (_fileLock)
        {
            document.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(document, JsonOptions());
            File.WriteAllText(_catalogPath, json);
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private NovelProjectInfo BuildDefaultProject(StoryBibleDocument bible)
    {
        var project = new NovelProjectInfo
        {
            Id = DefaultProjectId,
            Title = "当前小说",
            StorageProjectName = _workspace.ProjectName,
            Status = "Drafting",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        ApplyBibleMetadata(project, bible);
        return project;
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

    private static string Slugify(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), "[^\\p{L}\\p{Nd}]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
            slug = "novel";
        return slug.Length > 32 ? slug[..32] : slug;
    }

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
    public string StorageProjectName { get; set; } = string.Empty;
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

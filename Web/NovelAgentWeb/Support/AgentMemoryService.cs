using System.Text.Json;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentMemoryService
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly object _fileLock = new();

    public AgentMemoryService(NovelAgentWorkspace workspace) => _workspace = workspace;

    public Task HydrateAsync(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible, CancellationToken ct = default)
    {
        session.WorkingMemory.SessionMemory ??= new AgentSessionMemory();
        session.WorkingMemory.ProjectMemory = LoadProjectMemory(project) ?? BuildProjectMemory(project, bible);
        session.WorkingMemory.AuthorMemory = LoadAuthorMemory() ?? new AgentAuthorMemory();
        session.WorkingMemory.ExecutionMemory = LoadExecutionMemory(project) ?? new AgentExecutionMemory();
        MergeSessionPreferences(session);
        return Task.CompletedTask;
    }

    public Task PersistAsync(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible, AgentReflection? reflection = null, CancellationToken ct = default)
    {
        session.WorkingMemory.ProjectMemory ??= BuildProjectMemory(project, bible);
        session.WorkingMemory.AuthorMemory ??= new AgentAuthorMemory();
        session.WorkingMemory.ExecutionMemory ??= new AgentExecutionMemory();

        ApplyReflection(session, reflection);
        SaveProjectMemory(project, session.WorkingMemory.ProjectMemory);
        SaveAuthorMemory(session.WorkingMemory.AuthorMemory);
        SaveExecutionMemory(project, session.WorkingMemory.ExecutionMemory);
        return Task.CompletedTask;
    }

    public async Task<AgentRuntimeContext> LoadRuntimeContextAsync(
        SessionContext session,
        NovelProjectInfo project,
        CancellationToken ct)
    {
        var userProfile = await LoadUserProfileAsync(ct).ConfigureAwait(false);
        var projectMemory = await LoadProjectMemoryAsync(project, ct).ConfigureAwait(false);
        var executionMemory = await LoadExecutionMemoryAsync(project, ct).ConfigureAwait(false);

        return new AgentRuntimeContext
        {
            User = userProfile,
            ActiveProject = project,
            Session = session,
            Mission = new AgentMissionState(),
            MissionPlan = new AgentMissionPlan(),
        };
    }

    private async Task<UserProfile> LoadUserProfileAsync(CancellationToken ct)
    {
        var path = GetUserProfilePath();
        if (!File.Exists(path))
        {
            var defaultProfile = new UserProfile { UserId = "default" };
            await SaveUserProfileAsync(defaultProfile, ct).ConfigureAwait(false);
            return defaultProfile;
        }

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<UserProfile>(json) ?? new UserProfile();
    }

    private string GetUserProfilePath()
    {
        var root = StoragePathHelper.GetStorageRoot();
        var userDir = Path.Combine(root, "Users", "default");
        Directory.CreateDirectory(userDir);
        return Path.Combine(userDir, "profile.json");
    }

    private async Task SaveUserProfileAsync(UserProfile profile, CancellationToken ct)
    {
        var path = GetUserProfilePath();
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    private Task<AgentProjectMemory?> LoadProjectMemoryAsync(NovelProjectInfo project, CancellationToken ct)
    {
        return Task.FromResult(LoadProjectMemory(project));
    }

    private Task<AgentExecutionMemory?> LoadExecutionMemoryAsync(NovelProjectInfo project, CancellationToken ct)
    {
        return Task.FromResult(LoadExecutionMemory(project));
    }

    private static AgentProjectMemory BuildProjectMemory(NovelProjectInfo project, StoryBibleDocument bible)
    {
        var memory = new AgentProjectMemory
        {
            ProjectId = project.Id,
            LongTermGoal = project.CoreHook,
        };
        if (bible.Constitution != null)
        {
            memory.ReaderPromise = bible.Constitution.ReaderPromise;
            memory.Tone = $"{bible.Constitution.Genre}/{bible.Constitution.SubGenre}";
            memory.Constraints.AddRange(bible.Constitution.ForbiddenDirections.Take(8));
            memory.UnresolvedThreads.AddRange(bible.ForeshadowLedger
                .Where(f => !string.IsNullOrWhiteSpace(f.PlannedPayoffChapterId))
                .Select(f => $"{f.Name} -> {f.PlannedPayoffChapterId}")
                .Take(12));
        }
        return memory;
    }

    private static void MergeSessionPreferences(AgentSession session)
    {
        var memory = session.WorkingMemory;
        foreach (var preference in memory.UserPreferences.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!memory.SessionMemory.ShortTermPreferences.Contains(preference))
                memory.SessionMemory.ShortTermPreferences.Add(preference);
            if ((preference.Contains("不要") || preference.Contains("避免")) &&
                !memory.AuthorMemory.StyleDislikes.Contains(preference))
                memory.AuthorMemory.StyleDislikes.Add(preference);
        }

        Trim(memory.SessionMemory.ShortTermPreferences, 24);
        Trim(memory.AuthorMemory.StyleDislikes, 32);
    }

    private static void ApplyReflection(AgentSession session, AgentReflection? reflection)
    {
        if (reflection == null) return;
        var execution = session.WorkingMemory.ExecutionMemory;
        var author = session.WorkingMemory.AuthorMemory;
        if (reflection.QualityGate.Status is "needs_rewrite" or "fail")
        {
            var note = FirstNonEmpty(reflection.QualityGate.RewriteDecision, reflection.Summary);
            if (!string.IsNullOrWhiteSpace(note) && !execution.RepeatedBlockers.Contains(note))
                execution.RepeatedBlockers.Add(note);
        }
        if (reflection.QualityGate.Status == "pass" && !string.IsNullOrWhiteSpace(reflection.Summary))
        {
            var note = $"通过质量门禁：{reflection.Summary}";
            if (!execution.SuccessfulRepairNotes.Contains(note))
                execution.SuccessfulRepairNotes.Add(note);
        }
        foreach (var item in reflection.MissionPatch.ChapterPatches.Select(p => p.QualityIssueSummary).Where(s => !string.IsNullOrWhiteSpace(s)))
            if (!author.StyleDislikes.Contains(item) && item.Contains("文风", StringComparison.OrdinalIgnoreCase))
                author.StyleDislikes.Add(item);

        Trim(execution.RepeatedBlockers, 24);
        Trim(execution.SuccessfulRepairNotes, 24);
        Trim(author.StyleDislikes, 32);
    }

    private AgentProjectMemory? LoadProjectMemory(NovelProjectInfo project) =>
        Load<AgentProjectMemory>(ProjectMemoryPath(project));

    private AgentAuthorMemory? LoadAuthorMemory() =>
        Load<AgentAuthorMemory>(Path.Combine(GlobalAgentDir(), "author_memory.json"));

    private AgentExecutionMemory? LoadExecutionMemory(NovelProjectInfo project) =>
        Load<AgentExecutionMemory>(Path.Combine(ProjectAgentDir(project), "execution_memory.json"));

    private void SaveProjectMemory(NovelProjectInfo project, AgentProjectMemory memory) =>
        Save(ProjectMemoryPath(project), memory);

    private void SaveAuthorMemory(AgentAuthorMemory memory) =>
        Save(Path.Combine(GlobalAgentDir(), "author_memory.json"), memory);

    private void SaveExecutionMemory(NovelProjectInfo project, AgentExecutionMemory memory) =>
        Save(Path.Combine(ProjectAgentDir(project), "execution_memory.json"), memory);

    private string ProjectMemoryPath(NovelProjectInfo project) =>
        Path.Combine(ProjectAgentDir(project), "project_memory.json");

    private string ProjectAgentDir(NovelProjectInfo project)
    {
        var dir = Path.Combine(_workspace.StorageRoot, "Projects", project.StorageProjectName, "Agent");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string GlobalAgentDir()
    {
        var dir = Path.Combine(_workspace.StorageRoot, "Projects", _workspace.ProjectName, "Agent");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private T? Load<T>(string path)
    {
        lock (_fileLock)
        {
            if (!File.Exists(path)) return default;
            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions());
            }
            catch
            {
                return default;
            }
        }
    }

    private void Save<T>(string path, T value)
    {
        lock (_fileLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions()));
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static void Trim<T>(List<T> list, int max)
    {
        if (list.Count > max)
            list.RemoveRange(0, list.Count - max);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

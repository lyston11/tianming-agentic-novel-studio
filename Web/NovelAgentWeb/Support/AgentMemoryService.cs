using Microsoft.Extensions.Logging;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentMemoryService
{
    private readonly IAgentMemoryRepository _repository;
    private readonly ILogger<AgentMemoryService> _logger;

    // Magic numbers for list trimming
    private const int MaxForbiddenDirections = 8;
    private const int MaxUnresolvedThreads = 12;
    private const int MaxShortTermPreferences = 24;
    private const int MaxRepeatedBlockers = 24;
    private const int MaxSuccessfulRepairNotes = 24;
    private const int MaxStyleDislikes = 32;

    public AgentMemoryService(IAgentMemoryRepository repository, ILogger<AgentMemoryService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    // Compatibility stubs for workspace isolation pattern (no longer needed with DI)
    internal static void SetWorkspace(NovelAgentWorkspace workspace) { }
    internal static void ClearWorkspace() { }

    public async Task HydrateAsync(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible, CancellationToken ct = default)
    {
        var userId = session.UserId;
        session.WorkingMemory.SessionMemory ??= new AgentSessionMemory();

        var projectMem = await _repository.GetProjectMemoryAsync(userId, project.Id, ct);
        session.WorkingMemory.ProjectMemory = projectMem != null ? MapToAgentProjectMemory(projectMem, project.Id) : BuildProjectMemory(project, bible);

        var authorMem = await _repository.GetAuthorMemoryAsync(userId, ct);
        session.WorkingMemory.AuthorMemory = authorMem != null ? MapToAgentAuthorMemory(authorMem) : new AgentAuthorMemory();

        var executionMem = await _repository.GetExecutionMemoryAsync(userId, project.Id, ct);
        session.WorkingMemory.ExecutionMemory = executionMem != null ? MapToAgentExecutionMemory(executionMem) : new AgentExecutionMemory();

        MergeSessionPreferences(session);
    }

    public async Task PersistAsync(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible, AgentReflection? reflection = null, CancellationToken ct = default)
    {
        session.WorkingMemory.ProjectMemory ??= BuildProjectMemory(project, bible);
        session.WorkingMemory.AuthorMemory ??= new AgentAuthorMemory();
        session.WorkingMemory.ExecutionMemory ??= new AgentExecutionMemory();

        ApplyReflection(session, reflection);

        // Memory persistence will be implemented in Task 10 via ApplyMemoryUpdateAsync
        await Task.CompletedTask;
    }

    /// <summary>
    /// Applies memory updates from Reflection phase to repository.
    /// Note: SessionMemory sedimentation (preferences → constraints) requires access to AgentSession,
    /// which is not available here. This should be implemented in AgentRuntime.
    /// </summary>
    private async Task ApplyMemoryUpdateAsync(string userId, string projectId, AgentMemoryUpdate update, CancellationToken ct)
    {
        var updates = new Dictionary<string, object>();

        // SessionMemory: Updates are applied to in-memory session object
        // Sedimentation rule: Check if preferences repeated ≥3 times → sink to Constraints
        if (update.SessionMemory != null && update.SessionMemory.ExtractedPreferences.Count > 0)
        {
            // Note: SessionMemory sedimentation would require access to current AgentSession
            // This is complex because AgentMemoryService doesn't have direct access to session
            // For now, skip sedimentation logic - it can be handled in AgentRuntime where session is available
            // Just log the extracted preferences for Task 10
            _logger.LogDebug("Extracted {Count} preferences from session", update.SessionMemory.ExtractedPreferences.Count);
        }

        // ProjectMemory: Append new constraints and threads
        if (update.ProjectMemory != null)
        {
            if (update.ProjectMemory.NewConstraints.Count > 0)
            {
                var existing = await _repository.GetProjectMemoryAsync(userId, projectId, ct);
                var merged = existing.Constraints.Concat(update.ProjectMemory.NewConstraints).Distinct().ToList();
                updates["project.constraints"] = merged;
            }
            if (update.ProjectMemory.UnresolvedThreads.Count > 0)
            {
                var existing = await _repository.GetProjectMemoryAsync(userId, projectId, ct);
                var merged = existing.UnresolvedThreads.Concat(update.ProjectMemory.UnresolvedThreads).ToList();
                updates["project.unresolved_threads"] = merged;
            }
        }

        // AuthorMemory: Append style preferences (cross-project)
        if (update.AuthorMemory != null)
        {
            if (update.AuthorMemory.StyleLikes.Count > 0)
            {
                var existing = await _repository.GetAuthorMemoryAsync(userId, ct);
                var merged = existing.StyleLikes.Concat(update.AuthorMemory.StyleLikes).Distinct().ToList();
                updates["author.style_likes"] = merged;
            }
            if (update.AuthorMemory.StyleDislikes.Count > 0)
            {
                var existing = await _repository.GetAuthorMemoryAsync(userId, ct);
                var merged = existing.StyleDislikes.Concat(update.AuthorMemory.StyleDislikes).Distinct().ToList();
                updates["author.style_dislikes"] = merged;
            }
        }

        // ExecutionMemory: Append tool execution records
        if (update.ExecutionMemory != null)
        {
            if (!string.IsNullOrEmpty(update.ExecutionMemory.ToolSuccess))
            {
                var existing = await _repository.GetExecutionMemoryAsync(userId, projectId, ct);
                var merged = existing.SuccessfulRepairNotes.Append(update.ExecutionMemory.ToolSuccess).ToList();
                updates["execution.successful_repairs"] = merged;
            }
            if (!string.IsNullOrEmpty(update.ExecutionMemory.ToolFailure))
            {
                var existing = await _repository.GetExecutionMemoryAsync(userId, projectId, ct);
                var merged = existing.RepeatedBlockers.Append(update.ExecutionMemory.ToolFailure).ToList();
                updates["execution.repeated_blockers"] = merged;
            }
        }

        // Batch update all changes
        if (updates.Count > 0)
        {
            await _repository.UpdateMemoryAsync(userId, projectId, updates, ct);
            _logger.LogInformation("Applied {Count} memory updates for user {UserId}, project {ProjectId}", updates.Count, userId, projectId);
        }
    }

    public Task<AgentRuntimeContext> LoadRuntimeContextAsync(
        SessionContext session,
        NovelProjectInfo project,
        CancellationToken ct)
    {
        // Legacy method - not used, kept for compatibility
        return Task.FromResult(new AgentRuntimeContext
        {
            User = new UserProfile { UserId = "default" },
            ActiveProject = project,
            Session = session,
            Mission = new AgentMissionState(),
            MissionPlan = new AgentMissionPlan(),
        });
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
            memory.Constraints.AddRange(bible.Constitution.ForbiddenDirections.Take(MaxForbiddenDirections));
            memory.UnresolvedThreads.AddRange(bible.ForeshadowLedger
                .Where(f => !string.IsNullOrWhiteSpace(f.PlannedPayoffChapterId))
                .Select(f => $"{f.Name} -> {f.PlannedPayoffChapterId}")
                .Take(MaxUnresolvedThreads));
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

        Trim(memory.SessionMemory.ShortTermPreferences, MaxShortTermPreferences);
        Trim(memory.AuthorMemory.StyleDislikes, MaxStyleDislikes);
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

        Trim(execution.RepeatedBlockers, MaxRepeatedBlockers);
        Trim(execution.SuccessfulRepairNotes, MaxSuccessfulRepairNotes);
        Trim(author.StyleDislikes, MaxStyleDislikes);
    }

    private static void Trim<T>(List<T> list, int max)
    {
        if (list.Count > max)
            list.RemoveRange(0, list.Count - max);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static AgentProjectMemory MapToAgentProjectMemory(ProjectMemory source, string projectId) => new()
    {
        ProjectId = projectId,
        LongTermGoal = source.LongTermGoal ?? string.Empty,
        ReaderPromise = source.ReaderPromise ?? string.Empty,
        Tone = string.Empty,
        Constraints = new List<string>(source.Constraints),
        UnresolvedThreads = new List<string>(source.UnresolvedThreads)
    };

    private static AgentAuthorMemory MapToAgentAuthorMemory(AuthorMemory source) => new()
    {
        StyleLikes = new List<string>(source.StyleLikes),
        StyleDislikes = new List<string>(source.StyleDislikes),
        ConfirmationTolerance = source.ConfirmationTolerance ?? "key_checkpoints",
        GenreHabits = new List<string>(source.GenreHabits)
    };

    private static AgentExecutionMemory MapToAgentExecutionMemory(ExecutionMemory source) => new()
    {
        ToolFailurePatterns = new List<string>(source.ToolFailurePatterns),
        RepeatedBlockers = new List<string>(source.RepeatedBlockers),
        SuccessfulRepairNotes = new List<string>(source.SuccessfulRepairNotes)
    };
}

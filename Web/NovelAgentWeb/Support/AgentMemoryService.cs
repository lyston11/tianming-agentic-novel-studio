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
    private const int PreferenceSedimentationThreshold = 3;

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
        var transientSessionMemory = session.WorkingMemory.SessionMemory;

        var sessionMem = await _repository.GetSessionMemoryAsync(userId, project.Id, session.SessionId, ct);
        session.WorkingMemory.SessionMemory = sessionMem != null
            ? MapToAgentSessionMemory(sessionMem, transientSessionMemory)
            : transientSessionMemory;

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

        await ApplyMemoryUpdateAsync(session, project.Id, reflection?.MissionPatch.MemoryUpdate, ct);
    }

    /// <summary>
    /// Applies memory updates from Reflection phase to repository.
    /// </summary>
    private async Task ApplyMemoryUpdateAsync(AgentSession session, string projectId, AgentMemoryUpdate? update, CancellationToken ct)
    {
        var userId = session.UserId;
        var working = session.WorkingMemory;
        working.SessionMemory ??= new AgentSessionMemory();
        working.ProjectMemory ??= new AgentProjectMemory { ProjectId = projectId };
        working.AuthorMemory ??= new AgentAuthorMemory();
        working.ExecutionMemory ??= new AgentExecutionMemory();

        var updates = new Dictionary<string, object>();
        var authorUpdates = new Dictionary<string, object>();

        if (update?.SessionMemory != null)
        {
            if (!string.IsNullOrWhiteSpace(update.SessionMemory.ChatSummary))
                working.SessionMemory.ChatSummary = update.SessionMemory.ChatSummary.Trim();

            foreach (var preference in Clean(update.SessionMemory.ExtractedPreferences))
                working.SessionMemory.ShortTermPreferences.Add(preference);

            Trim(working.SessionMemory.ShortTermPreferences, MaxShortTermPreferences);
            var sedimented = working.SessionMemory.ShortTermPreferences
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= PreferenceSedimentationThreshold)
                .Select(g => g.Key)
                .ToList();

            foreach (var preference in sedimented)
                AddUnique(working.ProjectMemory.Constraints, preference);
        }

        if (update?.ProjectMemory != null)
        {
            foreach (var item in Clean(update.ProjectMemory.NewConstraints))
                AddUnique(working.ProjectMemory.Constraints, item);
            foreach (var item in Clean(update.ProjectMemory.UnresolvedThreads))
                AddUnique(working.ProjectMemory.UnresolvedThreads, item);
        }

        if (update?.AuthorMemory != null)
        {
            foreach (var item in Clean(update.AuthorMemory.StyleLikes))
                AddUnique(working.AuthorMemory.StyleLikes, item);
            foreach (var item in Clean(update.AuthorMemory.StyleDislikes))
                AddUnique(working.AuthorMemory.StyleDislikes, item);
        }

        if (update?.ExecutionMemory != null)
        {
            if (!string.IsNullOrWhiteSpace(update.ExecutionMemory.ToolSuccess))
                AddUnique(working.ExecutionMemory.SuccessfulRepairNotes, update.ExecutionMemory.ToolSuccess.Trim());
            if (!string.IsNullOrWhiteSpace(update.ExecutionMemory.ToolFailure))
                AddUnique(working.ExecutionMemory.RepeatedBlockers, update.ExecutionMemory.ToolFailure.Trim());
        }

        foreach (var id in Clean(update?.UsedKnowledgeIds))
            AddUnique(working.ProjectMemory.ReferencedKnowledgeIds, id);
        foreach (var pattern in Clean(update?.UsedTropePatterns))
            AddUnique(working.ProjectMemory.UsedTropePatterns, pattern);

        Trim(working.ProjectMemory.UnresolvedThreads, MaxUnresolvedThreads);
        Trim(working.ExecutionMemory.RepeatedBlockers, MaxRepeatedBlockers);
        Trim(working.ExecutionMemory.SuccessfulRepairNotes, MaxSuccessfulRepairNotes);
        Trim(working.AuthorMemory.StyleDislikes, MaxStyleDislikes);

        var hasExplicitUpdate = update != null;

        if (hasExplicitUpdate && working.ProjectMemory.Constraints.Count > 0)
            updates["project.constraints"] = working.ProjectMemory.Constraints.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hasExplicitUpdate && working.ProjectMemory.UnresolvedThreads.Count > 0)
            updates["project.unresolved_threads"] = working.ProjectMemory.UnresolvedThreads.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.ProjectMemory.ReferencedKnowledgeIds.Count > 0)
            updates["project.referenced_knowledge_ids"] = working.ProjectMemory.ReferencedKnowledgeIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.ProjectMemory.UsedTropePatterns.Count > 0)
            updates["project.used_trope_patterns"] = working.ProjectMemory.UsedTropePatterns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.ExecutionMemory.SuccessfulRepairNotes.Count > 0)
            updates["execution.successful_repairs"] = working.ExecutionMemory.SuccessfulRepairNotes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.ExecutionMemory.RepeatedBlockers.Count > 0)
            updates["execution.repeated_blockers"] = working.ExecutionMemory.RepeatedBlockers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hasExplicitUpdate && working.AuthorMemory.StyleLikes.Count > 0)
            authorUpdates["author.style_likes"] = working.AuthorMemory.StyleLikes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.AuthorMemory.StyleDislikes.Count > 0)
            authorUpdates["author.style_dislikes"] = working.AuthorMemory.StyleDislikes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (updates.Count > 0)
        {
            await _repository.UpdateMemoryAsync(userId, projectId, updates, ct);
        }

        if (authorUpdates.Count > 0)
        {
            await _repository.UpdateMemoryAsync(userId, null, authorUpdates, ct);
        }

        _logger.LogInformation(
            "Applied {ProjectCount} project/execution and {AuthorCount} author memory updates for user {UserId}, project {ProjectId}",
            updates.Count,
            authorUpdates.Count,
            userId,
            projectId);
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

    private static IEnumerable<string> Clean(IEnumerable<string>? values) =>
        values?
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v)) ?? Enumerable.Empty<string>();

    private static void AddUnique(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
            list.Add(value);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static AgentSessionMemory MapToAgentSessionMemory(SessionMemory source, AgentSessionMemory transient)
    {
        var memory = new AgentSessionMemory
        {
            CurrentGoal = FirstNonEmpty(transient.CurrentGoal, source.CurrentGoal),
            ChatSummary = transient.ChatSummary,
            PendingToolName = transient.PendingToolName ?? source.PendingToolName,
            LastIntent = transient.LastIntent ?? source.LastIntent
        };

        CopyDistinct(memory.OpenQuestions, source.OpenQuestions);
        CopyDistinct(memory.OpenQuestions, transient.OpenQuestions);
        CopyDistinct(memory.ShortTermPreferences, source.ShortTermPreferences);
        CopyDistinct(memory.ShortTermPreferences, transient.ShortTermPreferences);
        CopyDistinct(memory.RecentObservations, source.RecentObservations);
        CopyDistinct(memory.RecentObservations, transient.RecentObservations);
        CopyDistinct(memory.LastObservations, source.RecentObservations);
        CopyDistinct(memory.LastObservations, transient.LastObservations);
        CopyDistinct(memory.RecentUploadedKnowledgeIds, source.RecentUploadedKnowledgeIds);
        CopyDistinct(memory.RecentUploadedKnowledgeIds, transient.RecentUploadedKnowledgeIds);

        return memory;
    }

    private static AgentProjectMemory MapToAgentProjectMemory(ProjectMemory source, string projectId) => new()
    {
        ProjectId = projectId,
        LongTermGoal = source.LongTermGoal ?? string.Empty,
        ReaderPromise = source.ReaderPromise ?? string.Empty,
        Tone = string.Empty,
        Constraints = new List<string>(source.Constraints),
        UnresolvedThreads = new List<string>(source.UnresolvedThreads),
        ReferencedKnowledgeIds = new List<string>(source.ReferencedKnowledgeIds),
        ImportedKnowledgeIds = new List<string>(source.ImportedKnowledgeIds),
        KnowledgeInventory = new List<KnowledgeInventoryItem>(source.KnowledgeInventory),
        UsedTropePatterns = new List<string>(source.UsedTropePatterns)
    };

    private static AgentAuthorMemory MapToAgentAuthorMemory(AuthorMemory source) => new()
    {
        StyleLikes = new List<string>(source.StyleLikes),
        StyleDislikes = new List<string>(source.StyleDislikes),
        ConfirmationTolerance = source.ConfirmationTolerance ?? "key_checkpoints",
        GenreHabits = new List<string>(source.GenreHabits),
        FavoriteKnowledgeIds = new List<string>(source.FavoriteKnowledgeIds)
    };

    private static AgentExecutionMemory MapToAgentExecutionMemory(ExecutionMemory source) => new()
    {
        ToolFailurePatterns = new List<string>(source.ToolFailurePatterns),
        RepeatedBlockers = new List<string>(source.RepeatedBlockers),
        SuccessfulRepairNotes = new List<string>(source.SuccessfulRepairNotes),
        KnowledgeProcessingFailures = new List<string>(source.KnowledgeProcessingFailures)
    };

    private static void CopyDistinct(List<string> target, IEnumerable<string>? values)
    {
        foreach (var value in Clean(values))
            AddUnique(target, value);
    }
}

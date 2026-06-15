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
    private const int MaxSessionObservations = 24;
    private const int MaxRepeatedBlockers = 24;
    private const int MaxSuccessfulRepairNotes = 24;
    private const int MaxToolFailurePatterns = 24;
    private const int MaxKnowledgeProcessingFailures = 24;
    private const int MaxStyleDislikes = 32;
    private const int MaxGenreHabits = 24;
    private const int MaxFavoriteKnowledgeIds = 32;
    private const int PreferenceSedimentationThreshold = 3;

    public AgentMemoryService(
        IAgentMemoryRepository repository,
        ILogger<AgentMemoryService> logger,
        IAgentMemoryEventService? memoryEvents = null)
    {
        _repository = repository;
        _logger = logger;
        _memoryEvents = memoryEvents;
    }

    public async Task HydrateAsync(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible, CancellationToken ct = default)
    {
        var userId = session.UserId;
        session.WorkingMemory.SessionMemory ??= new AgentSessionMemory();
        var transientSessionMemory = session.WorkingMemory.SessionMemory;

        var sessionMem = await _repository.GetSessionMemoryAsync(userId, project.Id, session.SessionId, ct);
        session.WorkingMemory.SessionMemory = sessionMem != null
            ? MapToAgentSessionMemory(sessionMem, transientSessionMemory)
            : transientSessionMemory;
        ApplySessionMemoryToRuntime(session.WorkingMemory);

        var projectMem = await _repository.GetProjectMemoryAsync(userId, project.Id, ct);
        session.WorkingMemory.ProjectMemory = projectMem != null ? MapToAgentProjectMemory(projectMem, project.Id) : BuildProjectMemory(project, bible);
        ApplyProjectBaseline(session.WorkingMemory.ProjectMemory, BuildProjectMemory(project, bible));

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
        ApplyProjectBaseline(session.WorkingMemory.ProjectMemory, BuildProjectMemory(project, bible));

        ApplyReflection(session, reflection);

        await ApplyMemoryUpdateAsync(session, project.Id, reflection?.MissionPatch.MemoryUpdate, ct);
        await PersistSessionMemoryAsync(session, project.Id, reflection, ct);
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

        var currentProjectMemory = await _repository.GetProjectMemoryAsync(userId, projectId, ct) ?? new ProjectMemory();
        var currentAuthorMemory = await _repository.GetAuthorMemoryAsync(userId, ct) ?? new AuthorMemory();
        var currentExecutionMemory = await _repository.GetExecutionMemoryAsync(userId, projectId, ct) ?? new ExecutionMemory();

        var updates = new Dictionary<string, object>();
        var authorUpdates = new Dictionary<string, object>();
        var unionUpdates = new Dictionary<string, IReadOnlyList<string>>();
        var authorUnionUpdates = new Dictionary<string, IReadOnlyList<string>>();

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
            if (!string.IsNullOrWhiteSpace(update.AuthorMemory.ConfirmationTolerance))
                working.AuthorMemory.ConfirmationTolerance = update.AuthorMemory.ConfirmationTolerance.Trim();
            foreach (var item in Clean(update.AuthorMemory.GenreHabits))
                AddUnique(working.AuthorMemory.GenreHabits, item);
            foreach (var item in Clean(update.AuthorMemory.FavoriteKnowledgeIds))
                AddUnique(working.AuthorMemory.FavoriteKnowledgeIds, item);
        }

        if (update?.ExecutionMemory != null)
        {
            if (!string.IsNullOrWhiteSpace(update.ExecutionMemory.ToolSuccess))
                AddUnique(working.ExecutionMemory.SuccessfulRepairNotes, update.ExecutionMemory.ToolSuccess.Trim());
            if (!string.IsNullOrWhiteSpace(update.ExecutionMemory.ToolFailure))
                AddUnique(working.ExecutionMemory.RepeatedBlockers, update.ExecutionMemory.ToolFailure.Trim());
            foreach (var item in Clean(update.ExecutionMemory.ToolFailurePatterns))
                AddUnique(working.ExecutionMemory.ToolFailurePatterns, item);
            foreach (var item in Clean(update.ExecutionMemory.KnowledgeProcessingFailures))
                AddUnique(working.ExecutionMemory.KnowledgeProcessingFailures, item);
        }

        foreach (var id in Clean(update?.UsedKnowledgeIds))
            AddUnique(working.ProjectMemory.ReferencedKnowledgeIds, id);
        foreach (var pattern in Clean(update?.UsedTropePatterns))
            AddUnique(working.ProjectMemory.UsedTropePatterns, pattern);

        MergeCurrentThenWorking(working.ProjectMemory.ReferencedKnowledgeIds, currentProjectMemory.ReferencedKnowledgeIds);
        MergeCurrentThenWorking(working.ProjectMemory.UsedTropePatterns, currentProjectMemory.UsedTropePatterns);
        MergeCurrentThenWorking(working.ExecutionMemory.SuccessfulRepairNotes, currentExecutionMemory.SuccessfulRepairNotes);
        MergeCurrentThenWorking(working.ExecutionMemory.RepeatedBlockers, currentExecutionMemory.RepeatedBlockers);
        MergeCurrentThenWorking(working.ExecutionMemory.ToolFailurePatterns, currentExecutionMemory.ToolFailurePatterns);
        MergeCurrentThenWorking(working.ExecutionMemory.KnowledgeProcessingFailures, currentExecutionMemory.KnowledgeProcessingFailures);
        MergeCurrentThenWorking(working.AuthorMemory.StyleDislikes, currentAuthorMemory.StyleDislikes);
        MergeCurrentThenWorking(working.AuthorMemory.GenreHabits, currentAuthorMemory.GenreHabits);
        MergeCurrentThenWorking(working.AuthorMemory.FavoriteKnowledgeIds, currentAuthorMemory.FavoriteKnowledgeIds);

        Trim(working.ProjectMemory.UnresolvedThreads, MaxUnresolvedThreads);
        Trim(working.ExecutionMemory.RepeatedBlockers, MaxRepeatedBlockers);
        Trim(working.ExecutionMemory.SuccessfulRepairNotes, MaxSuccessfulRepairNotes);
        Trim(working.ExecutionMemory.ToolFailurePatterns, MaxToolFailurePatterns);
        Trim(working.ExecutionMemory.KnowledgeProcessingFailures, MaxKnowledgeProcessingFailures);
        Trim(working.AuthorMemory.StyleDislikes, MaxStyleDislikes);
        Trim(working.AuthorMemory.GenreHabits, MaxGenreHabits);
        Trim(working.AuthorMemory.FavoriteKnowledgeIds, MaxFavoriteKnowledgeIds);

        var hasExplicitUpdate = update != null;

        if (!string.IsNullOrWhiteSpace(working.ProjectMemory.LongTermGoal) &&
            !string.Equals(working.ProjectMemory.LongTermGoal, currentProjectMemory.LongTermGoal, StringComparison.Ordinal))
            updates["project.long_term_goal"] = working.ProjectMemory.LongTermGoal;
        if (!string.IsNullOrWhiteSpace(working.ProjectMemory.ReaderPromise) &&
            !string.Equals(working.ProjectMemory.ReaderPromise, currentProjectMemory.ReaderPromise, StringComparison.Ordinal))
            updates["project.reader_promise"] = working.ProjectMemory.ReaderPromise;
        if (hasExplicitUpdate && working.ProjectMemory.Constraints.Count > 0)
            updates["project.constraints"] = working.ProjectMemory.Constraints.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hasExplicitUpdate && working.ProjectMemory.UnresolvedThreads.Count > 0)
            updates["project.unresolved_threads"] = working.ProjectMemory.UnresolvedThreads.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.ProjectMemory.ReferencedKnowledgeIds.Count > 0)
            unionUpdates["project.referenced_knowledge_ids"] = working.ProjectMemory.ReferencedKnowledgeIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.ProjectMemory.UsedTropePatterns.Count > 0)
            unionUpdates["project.used_trope_patterns"] = working.ProjectMemory.UsedTropePatterns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.ExecutionMemory.SuccessfulRepairNotes.Count > 0)
            unionUpdates["execution.successful_repairs"] = working.ExecutionMemory.SuccessfulRepairNotes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.ExecutionMemory.RepeatedBlockers.Count > 0)
            unionUpdates["execution.repeated_blockers"] = working.ExecutionMemory.RepeatedBlockers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.ExecutionMemory.ToolFailurePatterns.Count > 0)
            unionUpdates["execution.tool_failures"] = working.ExecutionMemory.ToolFailurePatterns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.ExecutionMemory.KnowledgeProcessingFailures.Count > 0)
            unionUpdates["execution.knowledge_processing_failures"] = working.ExecutionMemory.KnowledgeProcessingFailures.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hasExplicitUpdate && working.AuthorMemory.StyleLikes.Count > 0)
            authorUpdates["author.style_likes"] = working.AuthorMemory.StyleLikes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hasExplicitUpdate && !string.IsNullOrWhiteSpace(working.AuthorMemory.ConfirmationTolerance))
            authorUpdates["author.confirmation_tolerance"] = working.AuthorMemory.ConfirmationTolerance;
        if (hasExplicitUpdate && working.AuthorMemory.GenreHabits.Count > 0)
            authorUpdates["author.genre_habits"] = working.AuthorMemory.GenreHabits.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hasExplicitUpdate && working.AuthorMemory.FavoriteKnowledgeIds.Count > 0)
            authorUpdates["author.favorite_knowledge_ids"] = working.AuthorMemory.FavoriteKnowledgeIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (working.AuthorMemory.StyleDislikes.Count > 0)
            authorUnionUpdates["author.style_dislikes"] = working.AuthorMemory.StyleDislikes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (updates.Count > 0)
        {
            await _repository.UpdateMemoryAsync(userId, projectId, updates, ct);
        }

        if (authorUpdates.Count > 0)
        {
            await _repository.UpdateMemoryAsync(userId, null, authorUpdates, ct);
        }

        if (unionUpdates.Count > 0)
        {
            await _repository.UnionMemoryAsync(userId, projectId, unionUpdates, ct);
        }

        if (authorUnionUpdates.Count > 0)
        {
            await _repository.UnionMemoryAsync(userId, null, authorUnionUpdates, ct);
        }

        _logger.LogInformation(
            "Applied {ProjectCount} project/execution, {AuthorCount} author, {UnionCount} project/execution union, and {AuthorUnionCount} author union memory updates for user {UserId}, project {ProjectId}",
            updates.Count,
            authorUpdates.Count,
            unionUpdates.Count,
            authorUnionUpdates.Count,
            userId,
            projectId);
    }

    private readonly IAgentMemoryEventService? _memoryEvents;

    private async Task PersistSessionMemoryAsync(AgentSession session, string projectId, AgentReflection? reflection, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(session.SessionId))
        {
            return;
        }

        SyncRuntimeToSessionMemory(session.WorkingMemory);
        var memory = session.WorkingMemory.SessionMemory;
        var updates = new Dictionary<string, object>
        {
            ["session.current_goal"] = memory.CurrentGoal,
            ["session.open_questions"] = memory.OpenQuestions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ["session.short_term_preferences"] = memory.ShortTermPreferences.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ["session.recent_observations"] = memory.RecentObservations.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ["session.pending_tool_name"] = memory.PendingToolName ?? string.Empty,
            ["session.last_intent"] = memory.LastIntent ?? string.Empty,
        };

        await _repository.UpdateSessionMemoryAsync(session.UserId, projectId, session.SessionId, updates, ct)
            .ConfigureAwait(false);

        if (_memoryEvents != null)
        {
            await _memoryEvents.AppendAsync(
                    session.UserId,
                    projectId,
                    session.SessionId,
                    session.ActiveRunId,
                    sourceType: "agent_runtime",
                    triggerType: reflection == null ? "runtime_session_memory" : "reflection_session_memory",
                    memoryScope: "session",
                    memoryKey: "runtime",
                    payload: new
                    {
                        currentGoal = memory.CurrentGoal,
                        pendingToolName = memory.PendingToolName,
                        lastIntent = memory.LastIntent,
                        openQuestionCount = memory.OpenQuestions.Count,
                        observationCount = memory.RecentObservations.Count
                    },
                    ct)
                .ConfigureAwait(false);
        }
    }

    private static void ApplySessionMemoryToRuntime(AgentWorkingMemory memory)
    {
        var session = memory.SessionMemory;
        memory.CurrentGoal = FirstNonEmpty(memory.CurrentGoal, session.CurrentGoal);
        CopyDistinct(memory.OpenQuestions, session.OpenQuestions);
        CopyDistinct(memory.UserPreferences, session.ShortTermPreferences);

        foreach (var item in Clean(session.RecentObservations))
        {
            if (memory.RecentObservations.Any(o => string.Equals(o.Message, item, StringComparison.OrdinalIgnoreCase)))
                continue;

            memory.RecentObservations.Add(new AgentRuntimeObservation
            {
                ObservationType = "session_memory",
                Message = item,
                Phase = "memory_restore",
                Success = true
            });
        }

        if (memory.RecentObservations.Count > MaxSessionObservations)
            memory.RecentObservations.RemoveRange(0, memory.RecentObservations.Count - MaxSessionObservations);
    }

    private static void SyncRuntimeToSessionMemory(AgentWorkingMemory memory)
    {
        memory.SessionMemory ??= new AgentSessionMemory();
        var session = memory.SessionMemory;

        session.CurrentGoal = FirstNonEmpty(memory.CurrentGoal, session.CurrentGoal);
        CopyDistinct(session.OpenQuestions, memory.OpenQuestions);
        CopyDistinct(session.ShortTermPreferences, memory.UserPreferences);
        foreach (var observation in memory.RecentObservations.Select(FormatObservation).Where(o => !string.IsNullOrWhiteSpace(o)))
            AddUnique(session.RecentObservations, observation);

        session.PendingToolName = memory.PendingToolCall?.Name;
        session.LastIntent = memory.LastDecision?.Intent ?? session.LastIntent;

        Trim(session.OpenQuestions, MaxUnresolvedThreads);
        Trim(session.ShortTermPreferences, MaxShortTermPreferences);
        Trim(session.RecentObservations, MaxSessionObservations);
    }

    private static string FormatObservation(AgentRuntimeObservation observation)
    {
        var head = FirstNonEmpty(observation.ToolName, observation.ObservationType, "observation");
        var body = FirstNonEmpty(observation.Message, observation.Phase);
        return string.IsNullOrWhiteSpace(body)
            ? head
            : $"{head}: {TrimText(body, 500)}";
    }

    private static string TrimText(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
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

    private static void ApplyProjectBaseline(AgentProjectMemory target, AgentProjectMemory baseline)
    {
        target.ProjectId = FirstNonEmpty(target.ProjectId, baseline.ProjectId);
        target.LongTermGoal = FirstNonEmpty(target.LongTermGoal, baseline.LongTermGoal);
        target.ReaderPromise = FirstNonEmpty(target.ReaderPromise, baseline.ReaderPromise);
        target.Tone = FirstNonEmpty(target.Tone, baseline.Tone);
        CopyDistinct(target.Constraints, baseline.Constraints);
        CopyDistinct(target.UnresolvedThreads, baseline.UnresolvedThreads);
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

    private static void MergeCurrentThenWorking(List<string> target, IEnumerable<string>? currentValues)
    {
        var workingValues = target.ToList();
        target.Clear();
        foreach (var value in Clean(currentValues))
            AddUnique(target, value);
        foreach (var value in Clean(workingValues))
            AddUnique(target, value);
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

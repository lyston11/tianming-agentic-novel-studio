using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Support;

public static class ProjectWorkflow
{
    public static async Task<ProjectWorkflowDocument?> BuildAsync(
        NovelAgentWorkspace workspace,
        NovelProjectCatalog catalog,
        AgentSessionManager sessionManager,
        MissionBlackboardRecoveryService? blackboardRecovery,
        string projectId,
        CancellationToken ct = default)
    {
        var project = await catalog.FindAsync(projectId, ct).ConfigureAwait(false);
        if (project == null)
            return null;

        var library = await NovelLibrary.BuildAsync(workspace, catalog, projectId, ct).ConfigureAwait(false);
        if (library == null)
            return null;

        var bible = await catalog.WithProjectAsync(
            project,
            () => workspace.Orchestrator.GetStoryBibleAsync(ct),
            ct).ConfigureAwait(false);

        var sessions = (await sessionManager.ListSessionsAsync(ct).ConfigureAwait(false))
            .Where(session => IsProjectSession(session, project.Id))
            .OrderByDescending(session => session.UpdatedAt)
            .ToList();

        if (blackboardRecovery != null)
        {
            foreach (var session in sessions)
                blackboardRecovery.Recover(session, project, bible);
        }

        var planDiagnostics = sessions
            .Select(session => new PlanDiagnostic(session.WorkingMemory.MissionPlan, BuildStaleMissionWarnings(project, session.WorkingMemory.MissionPlan).ToList()))
            .Where(item => IsProjectPlan(item.Plan, project.Id))
            .ToList();
        var missionPlans = planDiagnostics
            .Select(item => item.Plan)
            .ToList();
        var staleMissionWarnings = planDiagnostics
            .SelectMany(item => item.Warnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var healthyPlans = planDiagnostics
            .Where(item => item.Warnings.Count == 0)
            .Select(item => item.Plan)
            .ToList();

        var tasks = healthyPlans
            .SelectMany(plan => plan.SchedulerState?.Tasks ?? new List<AgentScheduledTask>())
            .Where(task => IsProjectTask(task, project.Id))
            .OrderBy(task => TaskRank(task.Status))
            .ThenByDescending(task => task.UpdatedAt)
            .ToList();
        var diagnosticTasks = planDiagnostics
            .Where(item => item.Warnings.Count > 0)
            .SelectMany(item => item.Plan.SchedulerState?.Tasks ?? new List<AgentScheduledTask>())
            .Where(task => IsProjectTask(task, project.Id))
            .OrderBy(task => TaskRank(task.Status))
            .ThenByDescending(task => task.UpdatedAt)
            .ToList();

        var activeSession = sessions.FirstOrDefault(session =>
                !string.IsNullOrWhiteSpace(session.ActiveRunId))
            ?? sessions.FirstOrDefault();

        var allArtifacts = BuildChapterArtifacts(bible.AgentRuns).ToList();
        var currentArtifacts = BuildCurrentChapterArtifacts(allArtifacts).ToList();
        var artifactTimeline = BuildArtifactTimeline(library, bible, allArtifacts, tasks).ToList();
        var productionStages = BuildProductionStages(library, artifactTimeline, tasks).ToList();
        var activityScore = ComputeActivityScore(library, bible, tasks, healthyPlans, currentArtifacts);
        var suspectReasons = BuildSuspectReasons(project, library, bible, tasks, missionPlans, staleMissionWarnings).ToList();
        var isEmptyProject = activityScore <= 0;

        return new ProjectWorkflowDocument(
            library.ActiveBook,
            library,
            sessions.Select(ToSessionSummary).ToList(),
            bible.AgentRuns.OrderByDescending(run => run.UpdatedAt).Select(ToRunSummary).ToList(),
            tasks,
            missionPlans,
            allArtifacts,
            currentArtifacts,
            diagnosticTasks,
            activityScore,
            isEmptyProject,
            suspectReasons,
            staleMissionWarnings,
            null,
            string.Empty,
            activeSession?.SessionId ?? string.Empty,
            activeSession?.ActiveRunId ?? tasks.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.RunId))?.RunId ?? string.Empty,
            DateTime.UtcNow.ToString("O"),
            Array.Empty<WorkflowCreativeIntentEvidence>(),
            productionStages,
            Array.Empty<WorkflowProductionChain>(),
            artifactTimeline)
        {
            RawRuns = bible.AgentRuns.OrderByDescending(run => run.UpdatedAt).ToList()
        };
    }

    private static WorkflowSessionSummary ToSessionSummary(AgentSession session) => new(
        session.SessionId,
        session.Title,
        session.Phase,
        session.ActiveRunId ?? string.Empty,
        session.UpdatedAt.ToString("O"),
        session.WorkingMemory.MissionPlan,
        null);

    private static WorkflowRunSummary ToRunSummary(NovelAgentRun run) => new(
        run.RunId,
        run.UserGoal,
        run.Intent.ToString(),
        run.Status.ToString(),
        run.TargetChapterId,
        run.CreatedAt.ToString("O"),
        run.UpdatedAt.ToString("O"),
        FirstNonEmpty(run.ChapterBrief?.SelectedCandidateTitle, run.ChapterBrief?.RecommendedCandidateTitle, run.TargetChapterId),
        run.DraftArtifact?.Status ?? string.Empty,
        run.GateReport?.Status ?? string.Empty,
        run.PostGenerationReview?.OverallResult ?? string.Empty,
        run.PostGenerationReview?.QualityScore ?? 0,
        run.Notes.ToList(),
        run.Steps.Select(step => new WorkflowRunStepSummary(
            step.Id,
            step.Name,
            step.Purpose,
            step.ToolName,
            step.Status.ToString(),
            step.RiskLevel.ToString(),
            step.RequiresConfirmation)).ToList());

    public static IReadOnlyList<WorkflowArtifactTimelineItem> BuildArtifactTimeline(
        NovelLibraryDocument library,
        StoryBibleDocument bible,
        IEnumerable<WorkflowChapterArtifactSummary> artifacts,
        IEnumerable<AgentScheduledTask> tasks,
        IEnumerable<WorkflowProductionEventSummary>? productionEvents = null)
    {
        var items = new List<WorkflowArtifactTimelineItem>();

        foreach (var run in bible.AgentRuns.OrderByDescending(run => run.UpdatedAt))
        {
            AddRunArtifacts(items, run);
        }

        foreach (var volume in bible.VolumeArcs.OrderByDescending(volume => volume.UpdatedAt))
            AddVolumePlan(items, volume, string.Empty, volume.UpdatedAt, volume.Status.ToString(), "StoryBible.VolumeArcs");

        foreach (var artifact in artifacts.OrderByDescending(artifact => ParseDate(artifact.UpdatedAt)))
        {
            if (items.Any(item => string.Equals(item.RunId, artifact.RunId, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(item.ChapterId, artifact.ChapterId, StringComparison.OrdinalIgnoreCase) &&
                                  item.Kind is "draft_artifact" or "gate_report" or "quality_review"))
                continue;

            items.Add(TimelineItem(
                $"chapter-artifact:{artifact.RunId}:{artifact.ChapterId}",
                "chapter_artifact_summary",
                "章节产物",
                "创作工作流",
                artifact.Status,
                FirstNonEmpty(artifact.CandidateTitle, artifact.ChapterId),
                artifact.HasDraft ? "章节已有草稿摘要。" : "章节已有过程产物。",
                FirstNonEmpty(artifact.DraftPreview, string.Join(" / ", artifact.GateIssues.Concat(artifact.QualityIssues).Take(4))),
                string.Empty,
                artifact.ChapterId,
                artifact.RunId,
                ParseDate(artifact.UpdatedAt),
                isFinal: string.Equals(artifact.DraftStatus, "committed", StringComparison.OrdinalIgnoreCase),
                isUserVisible: true,
                "ProjectWorkflow.ChapterArtifacts"));
        }

        foreach (var evt in (productionEvents ?? Array.Empty<WorkflowProductionEventSummary>())
                     .Where(IsOutputArtifactEvent)
                     .OrderByDescending(evt => ParseDate(evt.CreatedAt)))
        {
            var metadata = ParseOutputArtifactMetadata(evt.DataJson);
            var visibleInWorkflow = metadata.VisibleInWorkflow || metadata.UserVisibleWhere.Contains("创作工作流", StringComparer.Ordinal);
            var visibleInLibrary = metadata.VisibleInLibrary || metadata.UserVisibleWhere.Contains("小说书城", StringComparer.Ordinal);
            var surface = visibleInLibrary
                ? "小说书城"
                : visibleInWorkflow
                    ? "创作工作流"
                    : FirstNonEmpty(metadata.UserVisibleWhere.FirstOrDefault(), "创作工作流");
            var isFinal = visibleInLibrary ||
                          string.Equals(metadata.OutputKind, "FinalArtifact", StringComparison.OrdinalIgnoreCase);
            var sourceEvent = FirstNonEmpty(metadata.SourceEventType, evt.EventType);

            items.Add(TimelineItem(
                $"output-artifact:{FirstNonEmpty(evt.ArtifactId, evt.Id)}",
                FirstNonEmpty(evt.ArtifactType, "output_artifact"),
                isFinal ? "最终产物" : "过程产物",
                surface,
                evt.Status,
                FirstNonEmpty(evt.ArtifactType, evt.Message, "工具产物"),
                FirstNonEmpty(metadata.Summary, evt.Message),
                evt.DataJson,
                string.Empty,
                evt.ChapterId,
                evt.RuntimeRunId,
                ParseDate(evt.CreatedAt),
                isFinal,
                visibleInWorkflow || visibleInLibrary || metadata.UserVisibleWhere.Count > 0,
                $"OutputArtifactRecorder:{sourceEvent}"));
        }

        foreach (var volume in library.Volumes)
        {
            if (!string.Equals(volume.VolumeId, "unassigned", StringComparison.OrdinalIgnoreCase) &&
                !items.Any(item => item.Kind == "volume_plan" && string.Equals(item.VolumeId, volume.VolumeId, StringComparison.OrdinalIgnoreCase)))
            {
                items.Add(TimelineItem(
                    $"library-volume:{volume.VolumeId}",
                    "volume_plan",
                    "分卷规划",
                    "创作工作流",
                    volume.Status,
                    volume.Title,
                    $"{volume.Chapters.Count}/{volume.ExpectedChapterCount} 个章节位来自真实项目结构。",
                    string.Join(" / ", volume.Chapters.Select(chapter => chapter.Title).Where(v => !string.IsNullOrWhiteSpace(v)).Take(5)),
                    volume.VolumeId,
                    string.Empty,
                    string.Empty,
                    DateTime.MinValue,
                    isFinal: string.Equals(volume.Status, "Canon", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(volume.Status, "completed", StringComparison.OrdinalIgnoreCase),
                    isUserVisible: true,
                    "NovelLibrary.Volumes"));
            }

            foreach (var chapter in volume.Chapters.Where(IsLibraryChapterVisible))
            {
                items.Add(TimelineItem(
                    $"library-chapter:{chapter.ChapterId}",
                    "library_chapter",
                    "已入库章节",
                    "小说书城",
                    chapter.Status,
                    chapter.Title,
                    FirstNonEmpty(chapter.Summary, "正文已经进入小说书城。"),
                    chapter.Content,
                    volume.VolumeId,
                    chapter.ChapterId,
                    chapter.RunId,
                    ParseDate(chapter.UpdatedAt),
                    isFinal: true,
                    isUserVisible: true,
                    "NovelLibrary.Chapters"));
            }
        }

        foreach (var task in tasks.OrderByDescending(task => task.UpdatedAt))
        {
            if (task.Status is not ("running" or "blocked" or "queued"))
                continue;

            items.Add(new WorkflowArtifactTimelineItem(
                $"task:{task.TaskId}",
                "scheduled_task",
                "调度任务",
                "创作工作流",
                task.Status,
                FirstNonEmpty(task.ChapterId, task.TaskType),
                FirstNonEmpty(task.NextAction, task.BlockedReason, "等待 Agent 判断下一步。"),
                task.BlockedReason ?? string.Empty,
                string.Empty,
                task.ChapterId,
                task.RunId,
                task.UpdatedAt.ToString("O"),
                false,
                false,
                "AgentMissionPlan.SchedulerState"));
        }

        return NormalizeTimelineTitles(items, library)
            .OrderByDescending(item => ParseDate(item.UpdatedAt))
            .ToList();
    }

    private static bool IsOutputArtifactEvent(WorkflowProductionEventSummary evt) =>
        string.Equals(evt.EventType, OutputArtifactRecorder.EventType, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(evt.ArtifactId);

    private static OutputArtifactTimelineMetadata ParseOutputArtifactMetadata(string dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return new OutputArtifactTimelineMetadata();

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            var root = document.RootElement;
            return new OutputArtifactTimelineMetadata
            {
                OutputKind = GetJsonString(root, "outputKind"),
                Summary = GetJsonString(root, "summary"),
                SourceEventType = GetJsonString(root, "sourceEventType"),
                VisibleInWorkflow = GetJsonBool(root, "visibleInWorkflow"),
                VisibleInLibrary = GetJsonBool(root, "visibleInLibrary"),
                UserVisibleWhere = GetJsonStringArray(root, "userVisibleWhere")
            };
        }
        catch (JsonException)
        {
            return new OutputArtifactTimelineMetadata();
        }
    }

    private static string GetJsonString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetJsonBool(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static List<string> GetJsonStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private sealed class OutputArtifactTimelineMetadata
    {
        public string OutputKind { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string SourceEventType { get; init; } = string.Empty;
        public bool VisibleInWorkflow { get; init; }
        public bool VisibleInLibrary { get; init; }
        public List<string> UserVisibleWhere { get; init; } = new();
    }

    private static IEnumerable<WorkflowArtifactTimelineItem> NormalizeTimelineTitles(
        IEnumerable<WorkflowArtifactTimelineItem> items,
        NovelLibraryDocument library)
    {
        var chapterTitleById = library.Volumes
            .SelectMany(volume => volume.Chapters)
            .Where(chapter => !string.IsNullOrWhiteSpace(chapter.ChapterId))
            .GroupBy(chapter => chapter.ChapterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Title, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ChapterId) ||
                !chapterTitleById.TryGetValue(item.ChapterId, out var chapterTitle) ||
                string.IsNullOrWhiteSpace(chapterTitle))
            {
                yield return item;
                continue;
            }

            var title = chapterTitle;
            var summary = IsSameLooseText(item.Summary, title)
                ? item.IsFinal
                    ? "已进入小说书城，可在正文成稿中查看。"
                    : FirstNonEmpty(item.Status, "过程产物")
                : item.Summary;

            yield return item with { Title = title, Summary = summary };
        }
    }

    private static bool IsSameLooseText(string? left, string? right)
    {
        static string Normalize(string? value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        var normalizedLeft = Normalize(left);
        return normalizedLeft.Length > 0 && normalizedLeft == Normalize(right);
    }

    public static IReadOnlyList<WorkflowProductionStage> BuildProductionStages(
        NovelLibraryDocument library,
        IEnumerable<WorkflowArtifactTimelineItem> timeline,
        IEnumerable<AgentScheduledTask> tasks) =>
        BuildProductionStages(
            library,
            timeline,
            tasks,
            Array.Empty<WorkflowProductionEventSummary>());

    public static IReadOnlyList<WorkflowProductionStage> BuildProductionStages(
        NovelLibraryDocument library,
        IEnumerable<WorkflowArtifactTimelineItem> timeline,
        IEnumerable<AgentScheduledTask> tasks,
        IEnumerable<WorkflowProductionEventSummary> productionEvents) =>
        BuildProductionStages(
            library,
            timeline,
            tasks,
            productionEvents,
            Array.Empty<WorkflowToolExecutionSummary>());

    public static IReadOnlyList<WorkflowProductionStage> BuildProductionStages(
        NovelLibraryDocument library,
        IEnumerable<WorkflowArtifactTimelineItem> timeline,
        IEnumerable<AgentScheduledTask> tasks,
        IEnumerable<WorkflowProductionEventSummary> productionEvents,
        IEnumerable<WorkflowToolExecutionSummary> toolExecutions)
    {
        var items = timeline.ToList();
        var taskList = tasks.ToList();
        var eventList = productionEvents
            .OrderByDescending(e => ParseDate(e.CreatedAt))
            .ToList();
        var toolList = toolExecutions
            .OrderByDescending(t => ParseDate(t.StartedAt))
            .ToList();

        var committedChapterCount = library.Volumes
            .SelectMany(volume => volume.Chapters)
            .Count(IsLibraryChapterVisible);

        return new[]
        {
            BuildStage("foundation", "故事地基", "故事地基", items, taskList,
                item => item.Kind is "story_foundation_candidates" or "story_constitution",
                "还没有故事地基候选或 Story Bible。", "让 Agent 根据创作目标生成故事地基。",
                toolExecutions: toolList.Where(IsFoundationToolExecution).ToList()),
            BuildStage("volume_plan", "分卷规划", "创作工作流", items, taskList,
                item => item.Kind == "volume_plan",
                "还没有真实分卷规划。", "让 Agent 基于 Story Bible 规划第一卷。",
                toolExecutions: toolList.Where(IsVolumePlanToolExecution).ToList()),
            BuildStage("chapter_plan", "章节规划", "创作工作流", items, taskList,
                item => item.Kind == "chapter_brief",
                "还没有章节候选或章节目标。", "让 Agent 选择卷内章节并生成章节规划。", totalCount: library.PlannedChapterCount,
                toolExecutions: toolList.Where(IsChapterPlanToolExecution).ToList()),
            BuildStage("revision_plan", "修订计划", "创作工作流", items, taskList,
                item => item.Kind == "revision_plan",
                "还没有结构化修订计划。", "当用户或 Agent 要修改已提交章节时，先生成修订计划并分析下游影响。",
                productionEvents: eventList.Where(IsRevisionPlanProductionEvent).ToList(),
                toolExecutions: toolList.Where(IsRevisionPlanToolExecution).ToList()),
            BuildStage("context", "上下文包", "创作工作流", items, taskList,
                item => item.Kind == "context_package",
                "还没有章节上下文包。", "让 Agent 为目标章节构建上下文包。",
                productionEvents: eventList.Where(IsContextProductionEvent).ToList(),
                toolExecutions: toolList.Where(IsContextToolExecution).ToList()),
            BuildStage("draft", "正文草稿", "创作工作流", items, taskList,
                item => item.Kind == "draft_artifact" || item.Kind == "chapter_artifact_summary" && !string.IsNullOrWhiteSpace(item.Preview),
                "还没有真实正文草稿。", "让 Agent 在上下文包基础上生成正文草稿。",
                productionEvents: eventList.Where(IsDraftProductionEvent).ToList(),
                toolExecutions: toolList.Where(IsDraftToolExecution).ToList()),
            BuildStage("gate", "结构门禁", "创作工作流", items, taskList,
                item => item.Kind == "gate_report",
                "还没有结构门禁报告。", "草稿生成后由 Agent 执行结构门禁。",
                productionEvents: eventList.Where(IsGateProductionEvent).ToList(),
                toolExecutions: toolList.Where(IsGateToolExecution).ToList()),
            BuildStage("quality", "质量评审", "创作工作流", items, taskList,
                item => item.Kind == "quality_review",
                "还没有质量评审报告。", "门禁后由 Agent 执行质量评审。",
                productionEvents: eventList.Where(IsQualityProductionEvent).ToList(),
                toolExecutions: toolList.Where(IsQualityToolExecution).ToList()),
            BuildStage("library", "书城入库", "小说书城", items, taskList,
                item => item.Kind == "library_chapter" || item.IsFinal && item.Surface == "小说书城",
                "还没有已入库的真实章节。", "通过门禁和质量评审后，再提交到小说书城。",
                currentCount: committedChapterCount, totalCount: library.PlannedChapterCount,
                productionEvents: eventList.Where(IsLibraryProductionEvent).ToList(),
                toolExecutions: toolList.Where(IsLibraryToolExecution).ToList()),
            BuildStage("index", "后台索引", "系统后台", items, taskList,
                item => false,
                "还没有后台索引事件。", "正文入库后由后台索引与记忆沉淀继续处理。",
                productionEvents: eventList.Where(IsIndexProductionEvent).ToList(),
                toolExecutions: toolList.Where(IsIndexToolExecution).ToList())
        };
    }

    public static IReadOnlyList<WorkflowProductionChain> BuildProductionChains(
        IEnumerable<WorkflowProductionEventSummary> productionEvents)
    {
        return ProductionChainProjectionService.BuildWorkflowChainsCore(productionEvents);
    }

    private static bool IsContextProductionEvent(WorkflowProductionEventSummary evt) =>
        evt.EventType is "chapter_context_package_built" ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.PackageBuilt, StringComparison.OrdinalIgnoreCase);

    private static bool IsDraftProductionEvent(WorkflowProductionEventSummary evt) =>
        evt.EventType is "chapter_draft_generated" ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.DraftGenerated, StringComparison.OrdinalIgnoreCase);

    private static bool IsGateProductionEvent(WorkflowProductionEventSummary evt) =>
        evt.EventType is "chapter_gate_validated" ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.GateValidated, StringComparison.OrdinalIgnoreCase);

    private static bool IsQualityProductionEvent(WorkflowProductionEventSummary evt) =>
        evt.EventType is "chapter_quality_reviewed" ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.ReviewCompleted, StringComparison.OrdinalIgnoreCase);

    private static bool IsLibraryProductionEvent(WorkflowProductionEventSummary evt) =>
        !IsRevisionPlanProductionEvent(evt) &&
        (evt.EventType is "chapter_committed" ||
         evt.EventType is "knowledge_bindings_used" or "creative_intents_executed" or "chapter_continuity_facts_extracted" or "chapter_summary_recorded" ||
         string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.FactsPersisted, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.ChapterCommitted, StringComparison.OrdinalIgnoreCase));

    private static bool IsIndexProductionEvent(WorkflowProductionEventSummary evt) =>
        evt.EventType.StartsWith("outbox_", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(evt.Stage, "index_outbox", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.IndexUpdated, StringComparison.OrdinalIgnoreCase);

    private static bool IsRevisionPlanProductionEvent(WorkflowProductionEventSummary evt) =>
        evt.EventType.StartsWith("revision_plan_", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(evt.ArtifactType, "RevisionPlan", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(evt.Stage, "packages_invalidated", StringComparison.OrdinalIgnoreCase);

    private static bool IsFoundationToolExecution(WorkflowToolExecutionSummary tool) =>
        ToolNameIs(tool, "PlanStoryFoundation", "CommitStoryFoundation");

    private static bool IsVolumePlanToolExecution(WorkflowToolExecutionSummary tool) =>
        ToolNameIs(tool, "PlanVolumeArc", "CommitVolumeArc");

    private static bool IsChapterPlanToolExecution(WorkflowToolExecutionSummary tool) =>
        ToolNameIs(tool, "PlanChapter", "SelectChapterCandidate");

    private static bool IsRevisionPlanToolExecution(WorkflowToolExecutionSummary tool) =>
        ToolNameIs(tool, "CreateRevisionPlan", "InvalidateAffectedPackages", "AnalyzeDependencyImpact");

    private static bool IsContextToolExecution(WorkflowToolExecutionSummary tool) =>
        ToolNameIs(tool, NovelAgentProductionStages.ContextPackage) ||
        HasBlockedInputArtifacts(tool);

    private static bool IsDraftToolExecution(WorkflowToolExecutionSummary tool) =>
        ToolNameIs(tool, NovelAgentProductionStages.DraftGeneration, "ReviseCommittedChapter") ||
        ToolNameIs(tool, "ProduceChapter") && !HasBlockedInputArtifacts(tool);

    private static bool IsGateToolExecution(WorkflowToolExecutionSummary tool) =>
        ToolNameIs(tool, NovelAgentProductionStages.GateValidation, NovelAgentProductionStages.GateValidationOrRepair, NovelAgentProductionStages.DraftRepair);

    private static bool IsQualityToolExecution(WorkflowToolExecutionSummary tool) =>
        ToolNameIs(tool, NovelAgentProductionStages.QualityReview, "ReviewChapter", "AuditCommittedChapter");

    private static bool IsLibraryToolExecution(WorkflowToolExecutionSummary tool) =>
        ToolNameIs(tool, NovelAgentProductionStages.ChapterCommit, "CommitStoryFoundation", "CommitVolumeArc", "RollbackChapterVersion");

    private static bool IsIndexToolExecution(WorkflowToolExecutionSummary tool) =>
        ToolNameIs(tool, "RefreshProjectIndexes", "RetryProductionOutbox");

    private static bool ToolNameIs(WorkflowToolExecutionSummary tool, params string[] names) =>
        names.Any(name =>
            string.Equals(tool.ToolName, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tool.Phase, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsLibraryChapterVisible(NovelChapterView chapter) =>
        chapter.VisibleInLibrary ||
        string.Equals(chapter.ArtifactStatus, "committed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(chapter.WritingStatus, "committed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(chapter.Status, "committed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(chapter.Status, "published", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(chapter.Status, "completed", StringComparison.OrdinalIgnoreCase);

    private static void AddRunArtifacts(List<WorkflowArtifactTimelineItem> items, NovelAgentRun run)
    {
        if (run.Intent == NovelAgentIntent.CreateStoryFoundation && run.MacroCandidates.Count > 0)
        {
            var title = run.MacroCandidates.FirstOrDefault()?.Title ?? "故事地基候选";
            items.Add(TimelineItem(
                $"foundation-candidates:{run.RunId}",
                "story_foundation_candidates",
                "故事地基候选",
                "创作工作流",
                run.Status.ToString(),
                title,
                $"{run.MacroCandidates.Count} 个候选等待选择或固化。",
                FirstNonEmpty(run.MacroCandidates.FirstOrDefault()?.CoreHook, run.UserGoal),
                string.Empty,
                string.Empty,
                run.RunId,
                run.UpdatedAt,
                isFinal: false,
                isUserVisible: true,
                "StoryBible.AgentRuns"));
        }

        if (run.StoryConstitution != null)
        {
            items.Add(TimelineItem(
                $"constitution:{run.RunId}",
                "story_constitution",
                "Story Bible",
                "故事地基",
                run.Status.ToString(),
                FirstNonEmpty(run.StoryConstitution.CoreHook, "已生成创作宪法"),
                FirstNonEmpty(run.StoryConstitution.ReaderPromise, run.StoryConstitution.MainPleasure, "故事地基已生成。"),
                BuildConstitutionPreview(run.StoryConstitution),
                string.Empty,
                string.Empty,
                run.RunId,
                run.UpdatedAt,
                isFinal: run.Status == NovelAgentRunStatus.Completed,
                isUserVisible: true,
                "StoryBible.Constitution"));
        }

        if (run.VolumeArcPlan != null)
            AddVolumePlan(items, run.VolumeArcPlan, run.RunId, run.UpdatedAt, run.Status.ToString(), "StoryBible.AgentRuns.VolumeArcPlan");

        if (run.ChapterBrief != null)
        {
            var brief = run.ChapterBrief;
            items.Add(TimelineItem(
                $"chapter-brief:{run.RunId}:{brief.ChapterId}",
                "chapter_brief",
                "章节规划",
                "创作工作流",
                run.Status.ToString(),
                FirstNonEmpty(brief.SelectedCandidateTitle, brief.RecommendedCandidateTitle, brief.ChapterId),
                FirstNonEmpty(brief.CoreIdea, "章节候选或章节目标已生成。"),
                BuildChapterBriefPreview(brief),
                string.Empty,
                brief.ChapterId,
                run.RunId,
                run.UpdatedAt,
                isFinal: false,
                isUserVisible: true,
                "StoryBible.AgentRuns.ChapterBrief"));
        }

        if (run.ContextPackage != null)
        {
            var context = run.ContextPackage;
            items.Add(TimelineItem(
                $"context:{run.RunId}:{context.ChapterId}",
                "context_package",
                "上下文包",
                "创作工作流",
                context.Status,
                context.ChapterId,
                $"{context.WorldRules.Count + context.CharacterStates.Count + context.ActiveConflicts.Count} 条上下文约束，{context.LongDistanceRecall.Count} 条长距召回。",
                string.Join(" / ", context.Warnings.Concat(context.RagQueries).Where(v => !string.IsNullOrWhiteSpace(v)).Take(4)),
                string.Empty,
                context.ChapterId,
                run.RunId,
                context.BuiltAt,
                isFinal: false,
                isUserVisible: true,
                "StoryBible.AgentRuns.ContextPackage"));
        }

        if (run.DraftArtifact != null)
        {
            var draft = run.DraftArtifact;
            items.Add(TimelineItem(
                $"draft:{run.RunId}:{draft.ArtifactId}",
                "draft_artifact",
                "正文草稿",
                "创作工作流",
                draft.Status,
                FirstNonEmpty(run.ChapterBrief?.SelectedCandidateTitle, run.ChapterBrief?.RecommendedCandidateTitle, draft.ChapterId),
                string.IsNullOrWhiteSpace(draft.CommittedContent) ? "草稿仍在工作流中。" : "正文已经提交到书城。",
                BuildDraftPreview(draft),
                string.Empty,
                draft.ChapterId,
                run.RunId,
                draft.CommittedAt ?? draft.GeneratedAt,
                isFinal: string.Equals(draft.Status, "committed", StringComparison.OrdinalIgnoreCase),
                isUserVisible: true,
                "StoryBible.AgentRuns.DraftArtifact"));
        }

        if (run.GateReport != null)
        {
            var gate = run.GateReport;
            var chapterId = FirstNonEmpty(run.ChapterBrief?.ChapterId, run.TargetChapterId);
            items.Add(TimelineItem(
                $"gate:{run.RunId}:{gate.ValidatedAt:O}",
                "gate_report",
                "结构门禁",
                "创作工作流",
                gate.Status,
                chapterId,
                gate.Issues.Count == 0 ? "结构门禁暂无阻塞。" : $"{gate.Issues.Count} 个结构问题需要处理。",
                string.Join(" / ", gate.Issues.Concat(gate.RepairHints).Where(v => !string.IsNullOrWhiteSpace(v)).Take(4)),
                string.Empty,
                chapterId,
                run.RunId,
                gate.ValidatedAt,
                isFinal: string.Equals(gate.Status, "validated", StringComparison.OrdinalIgnoreCase),
                isUserVisible: true,
                "StoryBible.AgentRuns.GateReport"));
        }

        if (run.PostGenerationReview != null)
        {
            var review = run.PostGenerationReview;
            var chapterId = FirstNonEmpty(review.ChapterId, run.TargetChapterId);
            items.Add(TimelineItem(
                $"quality:{run.RunId}:{review.ReviewId}",
                "quality_review",
                "质量评审",
                "创作工作流",
                review.OverallResult,
                chapterId,
                review.RequiresRewrite ? "质量评审要求返工。" : "质量评审通过或无强阻塞。",
                FirstNonEmpty(review.Summary, string.Join(" / ", BuildQualityIssues(review).Take(4))),
                string.Empty,
                chapterId,
                run.RunId,
                review.CreatedAt,
                isFinal: !review.RequiresRewrite,
                isUserVisible: true,
                "StoryBible.AgentRuns.PostGenerationReview"));
        }
    }

    private static void AddVolumePlan(
        List<WorkflowArtifactTimelineItem> items,
        VolumeArcPlan volume,
        string runId,
        DateTime updatedAt,
        string status,
        string source)
    {
        var volumeId = FirstNonEmpty(volume.VolumeId, volume.Id);
        if (string.IsNullOrWhiteSpace(volumeId))
            return;

        if (items.Any(item => item.Kind == "volume_plan" && string.Equals(item.VolumeId, volumeId, StringComparison.OrdinalIgnoreCase)))
            return;

        items.Add(TimelineItem(
            $"volume:{volumeId}:{runId}",
            "volume_plan",
            "分卷规划",
            "创作工作流",
            status,
            FirstNonEmpty(volume.Title, "未命名卷"),
            FirstNonEmpty(volume.VolumePromise, volume.CoreQuestion, $"{volume.ExpectedChapterCount} 个章节位。"),
            BuildVolumePreview(volume),
            volumeId,
            string.Empty,
            runId,
            updatedAt,
            isFinal: volume.Status == VolumeArcStatus.Canon,
            isUserVisible: true,
            source));
    }

    private static WorkflowProductionStage BuildStage(
        string key,
        string label,
        string surface,
        IReadOnlyList<WorkflowArtifactTimelineItem> timeline,
        IReadOnlyList<AgentScheduledTask> tasks,
        Func<WorkflowArtifactTimelineItem, bool> predicate,
        string emptyReason,
        string nextIntentHint,
        int currentCount = 0,
        int totalCount = 0,
        IReadOnlyList<WorkflowProductionEventSummary>? productionEvents = null,
        IReadOnlyList<WorkflowToolExecutionSummary>? toolExecutions = null)
    {
        var artifacts = timeline.Where(predicate).OrderByDescending(item => ParseDate(item.UpdatedAt)).ToList();
        var events = (productionEvents ?? Array.Empty<WorkflowProductionEventSummary>())
            .OrderByDescending(e => ParseDate(e.CreatedAt))
            .ToList();
        var tools = (toolExecutions ?? Array.Empty<WorkflowToolExecutionSummary>())
            .OrderByDescending(t => ParseDate(t.StartedAt))
            .ToList();
        var activeTasks = tasks.Count(task => task.Status is "running" or "blocked" or "queued");
        var primary = artifacts.FirstOrDefault();
        var primaryEvent = events.FirstOrDefault();
        var primaryTool = tools.FirstOrDefault();
        var blocked = artifacts.Any(item =>
            item.Status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            item.Status.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
            item.Status.Contains("rewrite", StringComparison.OrdinalIgnoreCase)) ||
            events.Any(IsBlockedProductionEvent) ||
            tools.Any(IsBlockedToolExecution);
        var eventRunning = events.Any(evt =>
            evt.Status.Contains("running", StringComparison.OrdinalIgnoreCase) ||
            evt.Status.Contains("queued", StringComparison.OrdinalIgnoreCase) ||
            evt.Status.Contains("pending", StringComparison.OrdinalIgnoreCase));
        var toolRunning = tools.Any(tool =>
            tool.Status.Contains("running", StringComparison.OrdinalIgnoreCase) ||
            tool.Status.Contains("queued", StringComparison.OrdinalIgnoreCase) ||
            tool.Status.Contains("pending", StringComparison.OrdinalIgnoreCase));
        var eventReady = events.Count > 0 && events.All(evt =>
            evt.Status.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
            evt.Status.Contains("committed", StringComparison.OrdinalIgnoreCase) ||
            evt.Status.Contains("executed", StringComparison.OrdinalIgnoreCase));
        var toolReady = tools.Count > 0 && tools.All(tool =>
            tool.Status.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
            tool.Status.Contains("succeeded", StringComparison.OrdinalIgnoreCase));
        var latestEventReady = events.Count > 0 && (
            events[0].Status.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
            events[0].Status.Contains("committed", StringComparison.OrdinalIgnoreCase) ||
            events[0].Status.Contains("executed", StringComparison.OrdinalIgnoreCase));
        var latestToolReady = tools.Count > 0 && (
            tools[0].Status.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
            tools[0].Status.Contains("succeeded", StringComparison.OrdinalIgnoreCase));
        var running = activeTasks > 0 && artifacts.Any(item => item.Kind == "scheduled_task");
        var status = artifacts.Count == 0 && events.Count == 0 && tools.Count == 0
            ? "empty"
            : blocked
                ? "blocked"
                : running || eventRunning || toolRunning
                    ? "running"
                    : artifacts.Any(item => item.IsFinal) || eventReady || latestEventReady || toolReady || latestToolReady
                        ? "ready"
                        : "in_progress";
        var count = currentCount > 0 ? currentCount : artifacts.Count;
        var total = totalCount > 0 ? totalCount : artifacts.Count;
        if (events.Count > 0 && currentCount <= 0)
            count = events.Count(evt => evt.Status.Contains("completed", StringComparison.OrdinalIgnoreCase));
        if (events.Count > 0 && totalCount <= 0)
            total = events.Count;
        if (events.Count == 0 && tools.Count > 0 && currentCount <= 0)
            count = tools.Count(tool =>
                tool.Status.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
                tool.Status.Contains("succeeded", StringComparison.OrdinalIgnoreCase));
        if (events.Count == 0 && tools.Count > 0 && totalCount <= 0)
            total = tools.Count;

        var toolSummary = primaryTool == null
            ? string.Empty
            : FirstNonEmpty(primaryTool.SemanticContract.DisplayName, primaryTool.ToolName);

        return new WorkflowProductionStage(
            key,
            label,
            surface,
            status,
            primaryEvent?.Message ?? primary?.Summary ?? toolSummary ?? emptyReason,
            primaryEvent?.DataJson ?? primary?.Preview ?? BuildToolExecutionDetail(primaryTool),
            Math.Max(Math.Max(artifacts.Count, events.Count), tools.Count),
            count,
            total,
            primaryEvent?.CreatedAt ?? primary?.UpdatedAt ?? primaryTool?.StartedAt ?? string.Empty,
            primaryEvent?.ArtifactId ?? primary?.Id ?? primaryTool?.Id ?? string.Empty,
            primaryEvent?.RuntimeRunId ?? primary?.RunId ?? primaryTool?.RunId ?? string.Empty,
            artifacts.Count == 0 && events.Count == 0 && tools.Count == 0 ? emptyReason : string.Empty,
            nextIntentHint,
            events)
        {
            ToolExecutions = tools
        };
    }

    private static bool IsBlockedProductionEvent(WorkflowProductionEventSummary evt) =>
        evt.Status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
        evt.Status.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
        evt.Status.Contains("invalid", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedToolExecution(WorkflowToolExecutionSummary tool) =>
        tool.Status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
        tool.Status.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
        tool.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    private static string BuildToolExecutionDetail(WorkflowToolExecutionSummary? tool)
    {
        if (tool == null)
            return string.Empty;

        var blockedInputs = tool.Failure?.InputArtifacts
            .Where(artifact => artifact.BlocksExecution)
            .ToList();
        if (blockedInputs is { Count: > 0 })
        {
            return string.Join("；", blockedInputs.Select(artifact =>
            {
                var actions = artifact.RecommendedActions.Count == 0
                    ? string.Empty
                    : $"建议={string.Join("/", artifact.RecommendedActions.Take(3))}";
                return string.Join("，", new[]
                {
                    $"阻断产物={artifact.ArtifactName}",
                    $"状态={artifact.Status}",
                    string.IsNullOrWhiteSpace(artifact.ArtifactId) ? string.Empty : $"ID={artifact.ArtifactId}",
                    string.IsNullOrWhiteSpace(artifact.Message) ? string.Empty : artifact.Message,
                    actions
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
            }));
        }

        var inputs = tool.SemanticContract.InputArtifacts.Count == 0
            ? string.Empty
            : $"输入={string.Join("/", tool.SemanticContract.InputArtifacts.Take(4))}";
        var outputs = tool.SemanticContract.OutputArtifacts.Count == 0
            ? string.Empty
            : $"输出={string.Join("/", tool.SemanticContract.OutputArtifacts.Take(4))}";
        return string.Join("；", new[] { inputs, outputs }.Where(v => !string.IsNullOrWhiteSpace(v)));
    }

    private static bool HasBlockedInputArtifacts(WorkflowToolExecutionSummary tool) =>
        tool.Failure?.InputArtifacts.Any(artifact => artifact.BlocksExecution) == true;

    private static WorkflowArtifactTimelineItem TimelineItem(
        string id,
        string kind,
        string label,
        string surface,
        string status,
        string title,
        string summary,
        string preview,
        string volumeId,
        string chapterId,
        string runId,
        DateTime updatedAt,
        bool isFinal,
        bool isUserVisible,
        string source)
    {
        return new WorkflowArtifactTimelineItem(
            id,
            kind,
            label,
            surface,
            status,
            title,
            summary,
            TrimPreview(preview),
            volumeId,
            chapterId,
            runId,
            updatedAt == DateTime.MinValue ? string.Empty : updatedAt.ToString("O"),
            isFinal,
            isUserVisible,
            source);
    }

    private static string BuildConstitutionPreview(StoryCreativeConstitution constitution) =>
        string.Join(" / ", new[]
        {
            constitution.Genre,
            constitution.SubGenre,
            constitution.WorldCoreRule,
            constitution.MainConflictEngine,
            constitution.ProtagonistEngine
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Take(5));

    private static string BuildVolumePreview(VolumeArcPlan volume) =>
        string.Join(" / ", new[]
        {
            volume.EntryState,
            volume.CoreQuestion,
            volume.MainConflictUpgrade,
            volume.Climax,
            volume.AftermathHook
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Take(5));

    private static string BuildChapterBriefPreview(ChapterCreativeBrief brief) =>
        string.Join(" / ", new[]
        {
            brief.ConflictMove,
            brief.CharacterChoice,
            brief.CostOrConsequence,
            brief.ForeshadowingAction
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Take(5));

    private static string TrimPreview(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var normalized = value.Trim();
        return normalized.Length <= 1200 ? normalized : normalized[..1200] + "...";
    }

    private static IEnumerable<WorkflowChapterArtifactSummary> BuildChapterArtifacts(IEnumerable<NovelAgentRun> runs)
    {
        foreach (var run in runs.Where(run => !string.IsNullOrWhiteSpace(run.TargetChapterId)).OrderByDescending(run => run.UpdatedAt))
        {
            var review = run.PostGenerationReview;
            yield return new WorkflowChapterArtifactSummary(
                run.TargetChapterId,
                run.RunId,
                run.Intent.ToString(),
                run.Status.ToString(),
                run.UpdatedAt.ToString("O"),
                FirstNonEmpty(run.ChapterBrief?.SelectedCandidateTitle, run.ChapterBrief?.RecommendedCandidateTitle),
                run.DraftArtifact?.Status ?? string.Empty,
                BuildDraftPreview(run.DraftArtifact),
                !string.IsNullOrWhiteSpace(run.DraftArtifact?.DraftContent),
                run.GateReport?.Status ?? string.Empty,
                run.GateReport?.Issues.ToList() ?? new List<string>(),
                run.GateReport?.RepairHints.ToList() ?? new List<string>(),
                review?.OverallResult ?? string.Empty,
                review?.QualityScore ?? 0,
                BuildQualityIssues(review).ToList(),
                run.ContextPackage?.Warnings.ToList() ?? new List<string>(),
                BuildDependencyWarnings(run.DependencyImpact).ToList(),
                run.ContextPackage?.LongDistanceRecall.Count ?? 0,
                new[] { run.RunId },
                ComputeLifecycleRank(run),
                false);
        }
    }

    private static IEnumerable<WorkflowChapterArtifactSummary> BuildCurrentChapterArtifacts(List<WorkflowChapterArtifactSummary> artifacts)
    {
        foreach (var group in artifacts.GroupBy(artifact => artifact.ChapterId, StringComparer.OrdinalIgnoreCase))
        {
            var sourceRunIds = group
                .SelectMany(artifact => artifact.SourceRunIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var current = group
                .OrderByDescending(artifact => artifact.LifecycleRank)
                .ThenByDescending(artifact => ParseDate(artifact.UpdatedAt))
                .First();
            yield return current with
            {
                SourceRunIds = sourceRunIds,
                IsCurrent = true,
            };
        }
    }

    private static IEnumerable<string> BuildQualityIssues(NovelAgentPostGenerationReview? review)
    {
        if (review == null)
            yield break;

        if (review.RequiresRewrite && !string.IsNullOrWhiteSpace(review.Summary))
            yield return review.Summary;

        foreach (var check in review.Checks.Where(check =>
                     !string.Equals(check.Status.ToString(), "Pass", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(check.Status.ToString(), "Passed", StringComparison.OrdinalIgnoreCase)))
        {
            yield return $"{check.Name}：{check.Status} · {check.Message}";
        }
    }

    private static IEnumerable<string> BuildDependencyWarnings(DependencyImpactReport? impact)
    {
        if (impact == null)
            yield break;

        if (!string.IsNullOrWhiteSpace(impact.Summary))
            yield return impact.Summary;

        foreach (var chapterId in impact.ImpactedChapters.Take(6))
            yield return $"影响章节：{chapterId}";
    }

    private static bool IsProjectSession(AgentSession session, string projectId) =>
        string.Equals(session.ActiveProjectId, projectId, StringComparison.OrdinalIgnoreCase) ||
        IsProjectPlan(session.WorkingMemory.MissionPlan, projectId);

    private static bool IsProjectPlan(AgentMissionPlan? plan, string projectId) =>
        plan != null &&
        (string.Equals(plan.ProjectId, projectId, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(plan.BookTaskTree?.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));

    private static bool IsProjectTask(AgentScheduledTask task, string projectId) =>
        string.Equals(task.ProjectId, projectId, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> BuildStaleMissionWarnings(NovelProjectInfo project, AgentMissionPlan? plan)
    {
        if (plan == null || !IsProjectPlan(plan, project.Id))
            yield break;

        var projectTitle = NormalizeComparable(project.Title);
        var planTitle = NormalizeComparable(plan.ProjectTitle);
        var treeTitle = NormalizeComparable(plan.BookTaskTree?.Title);
        var volumeTitles = plan.BookTaskTree?.Volumes
            .Select(volume => NormalizeComparable(volume.Title))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList() ?? new List<string>();

        if (!string.IsNullOrWhiteSpace(projectTitle) &&
            !string.IsNullOrWhiteSpace(planTitle) &&
            !string.Equals(projectTitle, planTitle, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"任务黑板标题「{plan.ProjectTitle}」与项目「{project.Title}」不一致。";
        }

        if (!string.IsNullOrWhiteSpace(projectTitle) &&
            !string.IsNullOrWhiteSpace(treeTitle) &&
            !string.Equals(projectTitle, treeTitle, StringComparison.OrdinalIgnoreCase) &&
            !IsGenericUntitled(projectTitle))
        {
            yield return $"任务树标题「{plan.BookTaskTree?.Title}」与项目「{project.Title}」不一致。";
        }

        if (IsGenericUntitled(projectTitle) && volumeTitles.Any(title => !IsGenericUntitled(title) && !ContainsNormalized(project.CoreHook, title)))
            yield return $"未命名项目包含历史卷结构：{string.Join("、", volumeTitles.Take(2))}。";
    }

    private static IEnumerable<string> BuildSuspectReasons(
        NovelProjectInfo project,
        NovelLibraryDocument library,
        StoryBibleDocument bible,
        List<AgentScheduledTask> tasks,
        List<AgentMissionPlan> missionPlans,
        List<string> staleMissionWarnings)
    {
        if ((library.PlannedChapterCount + library.GeneratedChapterCount) == 0 && bible.AgentRuns.Count == 0 && tasks.Count == 0)
            yield return "空项目：暂无会话任务、章节 run 或草稿 artifact。";
        if (staleMissionWarnings.Count > 0)
            yield return "历史黑板疑似污染，已从主调度队列隔离。";
        if (missionPlans.Count == 0 && !string.Equals(project.Id, "default", StringComparison.OrdinalIgnoreCase))
            yield return "暂无绑定的任务黑板。";
    }

    private static int ComputeActivityScore(
        NovelLibraryDocument library,
        StoryBibleDocument bible,
        List<AgentScheduledTask> tasks,
        List<AgentMissionPlan> healthyPlans,
        List<WorkflowChapterArtifactSummary> currentArtifacts)
    {
        var activeTasks = tasks.Count(task => task.Status is "running" or "blocked");
        return activeTasks * 1000
               + currentArtifacts.Count * 120
               + bible.AgentRuns.Count * 80
               + healthyPlans.Sum(plan => plan.BookTaskTree?.Volumes.Sum(volume => volume.Chapters.Count) ?? 0) * 20
               + library.PlannedChapterCount * 10
               + library.GeneratedChapterCount * 200;
    }

    private static int ComputeLifecycleRank(NovelAgentRun run)
    {
        if (string.Equals(run.DraftArtifact?.Status, "committed", StringComparison.OrdinalIgnoreCase))
            return 700;
        if (run.PostGenerationReview != null && !run.PostGenerationReview.RequiresRewrite)
            return 650;
        if (run.PostGenerationReview?.RequiresRewrite == true)
            return 620;
        if (string.Equals(run.GateReport?.Status, "validated", StringComparison.OrdinalIgnoreCase))
            return 600;
        if (run.GateReport != null)
            return 520;
        if (!string.IsNullOrWhiteSpace(run.DraftArtifact?.DraftContent))
            return 500;
        if (run.ContextPackage != null)
            return 400;
        if (run.ChapterBrief?.Candidates.Count > 0)
            return 300;
        if (run.ChapterBrief != null)
            return 200;
        return 100;
    }

    private static int TaskRank(string status) => status.ToLowerInvariant() switch
    {
        "running" => 0,
        "blocked" => 1,
        "queued" => 2,
        "paused" => 4,
        "done" => 5,
        _ => 6,
    };

    private static string BuildDraftPreview(ChapterDraftArtifact? draft)
    {
        var content = draft?.DraftContent ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var compact = string.Join(
            "\n\n",
            content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3));
        return compact.Length <= 1600 ? compact : compact[..1600] + "...";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, out var parsed) ? parsed : DateTime.MinValue;

    private static string NormalizeComparable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).Trim().ToLowerInvariant();
    }

    private static bool IsGenericUntitled(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("未命名", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("当前小说", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsNormalized(string? source, string normalizedNeedle)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(normalizedNeedle))
            return false;
        return NormalizeComparable(source).Contains(normalizedNeedle, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PlanDiagnostic(AgentMissionPlan Plan, List<string> Warnings);
}

using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Support;

public static class ProjectWorkflow
{
    public static async Task<ProjectWorkflowDocument?> BuildAsync(
        NovelAgentWorkspace workspace,
        NovelProjectCatalog catalog,
        AgentSessionManager sessionManager,
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
        var activityScore = ComputeActivityScore(library, bible, tasks, healthyPlans, currentArtifacts);
        var suspectReasons = BuildSuspectReasons(project, library, bible, tasks, missionPlans, staleMissionWarnings).ToList();
        var isEmptyProject = activityScore <= 0;

        return new ProjectWorkflowDocument(
            library.ActiveBook,
            library,
            sessions.Select(ToSessionSummary).ToList(),
            bible.AgentRuns.OrderByDescending(run => run.UpdatedAt).ToList(),
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
            DateTime.UtcNow.ToString("O"));
    }

    private static WorkflowSessionSummary ToSessionSummary(AgentSession session) => new(
        session.SessionId,
        session.Title,
        session.Phase,
        session.ActiveRunId ?? string.Empty,
        session.UpdatedAt.ToString("O"),
        session.WorkingMemory.MissionPlan,
        null);

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

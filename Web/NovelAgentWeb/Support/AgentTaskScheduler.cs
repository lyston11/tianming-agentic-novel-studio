using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentTaskScheduler
{
    private static readonly HashSet<string> HighRiskActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProduceChapter",
        "CommitStoryFoundation",
        "CommitVolumeArc",
    };

    public AgentScheduledTask? SelectActiveTask(AgentMissionPlan plan)
    {
        var tasks = plan.SchedulerState?.Tasks ?? new List<AgentScheduledTask>();
        return tasks.FirstOrDefault(t => t.Status == "running") ??
               tasks.FirstOrDefault(t => t.Status == "queued") ??
               tasks.FirstOrDefault(t => t.Status == "blocked");
    }

    public AgentAction? BuildContinueAction(AgentObservationContext context)
    {
        var focusedRunId = LatestProjectContentRunId(context);
        var requestedChapterId = LatestRequestedChapterId(context);
        var repairQuery = context.RecentObservations
            .Where(o => o.IsRepairable && !string.IsNullOrWhiteSpace(o.RecommendedToolName))
            .Where(o => string.IsNullOrWhiteSpace(focusedRunId) ||
                        string.Equals(o.RunId, focusedRunId, StringComparison.OrdinalIgnoreCase))
            .Where(o => string.IsNullOrWhiteSpace(requestedChapterId) ||
                        ObservationMatchesChapter(context.MissionPlan, o, requestedChapterId));
        var repair = repairQuery.OrderByDescending(o => o.CreatedAt).FirstOrDefault();
        if (repair != null)
            return BuildActionFromRepairObservation(repair);

        var task = !string.IsNullOrWhiteSpace(focusedRunId)
            ? SelectActiveTaskForRun(context.MissionPlan, focusedRunId)
            : !string.IsNullOrWhiteSpace(requestedChapterId)
                ? SelectActiveTaskForChapter(context.MissionPlan, requestedChapterId)
                : SelectActiveTask(context.MissionPlan);
        if (task == null || string.IsNullOrWhiteSpace(task.NextAction))
            return null;

        if (task.Status == "blocked")
        {
            return new AgentAction
            {
                Type = AgentActionType.FinalReply,
                Intent = "mission_blocked",
                Reply = string.IsNullOrWhiteSpace(task.BlockedReason)
                    ? "当前任务被阻塞，需要先查看任务状态或补充设定。"
                    : task.BlockedReason,
                Suggestions = new[] { "查看当前状态", "补充设定", "分析依赖影响" },
                Source = "agent_task_scheduler",
            };
        }

        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ToolNeedsRunId(task.NextAction) && !string.IsNullOrWhiteSpace(task.RunId))
            args["runId"] = task.RunId;
        if (string.Equals(task.NextAction, "PlanChapter", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentAction
            {
                Type = AgentActionType.FinalReply,
                Intent = "chapter_planning_needs_agent_directions",
                Reply = "章节规划需要先由 Agent 整理 creativeBrief 和 candidateDirections；我不会从用户原话里自动推断候选方向。",
                Suggestions = new[] { "补充章节创作方向", "查看当前状态" },
                Source = "agent_task_scheduler",
            };
        }

        return new AgentAction
        {
            Type = AgentActionType.ToolCall,
            Intent = "continue_mission",
            ToolCall = new AgentToolCall { Name = task.NextAction, Arguments = args },
            Risk = task.Risk,
            RequiresConfirmation = false,
            Brief = $"Scheduler selected {task.NextAction} for {task.ChapterId}.",
            Reply = string.Empty,
            Source = "agent_task_scheduler",
        };
    }

    private static AgentAction BuildActionFromRepairObservation(AgentRuntimeObservation repair)
    {
        var toolName = repair.RecommendedToolName.Trim();
        var args = new Dictionary<string, string>(repair.RecommendedArguments, StringComparer.OrdinalIgnoreCase);
        if (ToolNeedsRunId(toolName) && !args.ContainsKey("runId") && !string.IsNullOrWhiteSpace(repair.RunId))
            args["runId"] = repair.RunId;

        return new AgentAction
        {
            Type = AgentActionType.ToolCall,
            Intent = "continue_repairable_prerequisite",
            ToolCall = new AgentToolCall { Name = toolName, Arguments = args },
            Risk = HighRiskActions.Contains(toolName) ? "High" : "Medium",
            RequiresConfirmation = false,
            Brief = $"Scheduler selected repair prerequisite {toolName}.",
            Reply = string.Empty,
            Source = "agent_task_scheduler_repair",
        };
    }

    public void MarkWaitingConfirmation(AgentMissionPlan plan, AgentToolCall call, string runId)
    {
        var task = plan.SchedulerState.Tasks.FirstOrDefault(t =>
            string.Equals(t.RunId, runId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.NextAction, call.Name, StringComparison.OrdinalIgnoreCase));
        if (task == null)
            return;
        task.Status = "queued";
        task.RequiresConfirmation = false;
        task.Reason = "agent_loop_resume";
        task.UpdatedAt = DateTime.UtcNow;
        plan.SchedulerState.ActiveTaskId = task.TaskId;
        plan.SchedulerState.ActiveRunId = task.RunId;
        plan.SchedulerState.ActiveChapterId = task.ChapterId;
        plan.SchedulerState.LastDecisionReason = task.Reason;
        plan.SchedulerState.UpdatedAt = DateTime.UtcNow;
    }

    private static bool ToolNeedsRunId(string toolName) =>
        toolName is "ProduceChapter" or "SelectChapterCandidate" or "RefreshProjectIndexes" or
            "AnalyzeDependencyImpact" or "ReviewChapter" or "CommitVolumeArc" or "CommitStoryFoundation";

    private static AgentScheduledTask? SelectActiveTaskForRun(AgentMissionPlan plan, string runId)
    {
        var tasks = plan.SchedulerState?.Tasks ?? new List<AgentScheduledTask>();
        return tasks.Where(t => string.Equals(t.RunId, runId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Status == "running" ? 0 : t.Status == "queued" ? 1 : t.Status == "blocked" ? 2 : 3)
            .FirstOrDefault(t => t.Status is "running" or "queued" or "blocked");
    }

    private static AgentScheduledTask? SelectActiveTaskForChapter(AgentMissionPlan plan, string chapterId)
    {
        var normalized = AgentChapterReferenceResolver.NormalizeChapterId(chapterId);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var tasks = plan.SchedulerState?.Tasks ?? new List<AgentScheduledTask>();
        return tasks.Where(t => string.Equals(
                AgentChapterReferenceResolver.NormalizeChapterId(t.ChapterId),
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Status == "running" ? 0 : t.Status == "queued" ? 1 : t.Status == "blocked" ? 2 : 3)
            .FirstOrDefault(t => t.Status is "running" or "queued" or "blocked");
    }

    private static bool ObservationMatchesChapter(AgentMissionPlan plan, AgentRuntimeObservation observation, string chapterId)
    {
        var normalized = AgentChapterReferenceResolver.NormalizeChapterId(chapterId);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var artifactChapterId = AgentChapterReferenceResolver.NormalizeChapterId(observation.Artifact?.ArtifactId);
        if (string.Equals(artifactChapterId, normalized, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(observation.RunId))
            return false;

        return plan.SchedulerState.Tasks.Any(t =>
            string.Equals(t.RunId, observation.RunId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                AgentChapterReferenceResolver.NormalizeChapterId(t.ChapterId),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string LatestRequestedChapterId(AgentObservationContext context) =>
        FirstNonEmpty(
            AgentChapterReferenceResolver.NormalizeChapterId(context.TurnIntent?.ReferencedChapterId),
            AgentChapterReferenceResolver.NormalizeChapterId(context.UserTurn?.TargetArtifact),
            AgentChapterReferenceResolver.ResolveChapterId(context.UserMessage),
            AgentChapterReferenceResolver.ResolveChapterId(context.TurnIntent?.RawMessage ?? string.Empty));

    private static string LatestProjectContentRunId(AgentObservationContext context)
    {
        var observationRunId = context.RecentObservations
            .Where(o => o.Success &&
                        string.Equals(o.Artifact?.ArtifactType, "project_content_query", StringComparison.OrdinalIgnoreCase))
            .Select(o => FirstNonEmpty(o.RunId, o.Artifact?.RunId))
            .LastOrDefault(runId => !string.IsNullOrWhiteSpace(runId));
        if (!string.IsNullOrWhiteSpace(observationRunId))
            return observationRunId;

        return context.MissionPlan.ActiveArtifacts
            .Where(a => string.Equals(a.ArtifactType, "project_content_query", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.RunId)
            .LastOrDefault(runId => !string.IsNullOrWhiteSpace(runId)) ?? string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

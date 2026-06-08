using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentTaskScheduler
{
    private static readonly HashSet<string> HighRiskActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "GenerateChapterWithChanges",
        "RepairChapterDraft",
        "CommitValidatedChapter",
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
        var repair = context.RecentObservations
            .Where(o => o.IsRepairable && !string.IsNullOrWhiteSpace(o.RecommendedToolName))
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefault();
        if (repair != null)
            return BuildActionFromRepairObservation(repair);

        var task = SelectActiveTask(context.MissionPlan);
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
        if (string.Equals(task.NextAction, "PlanChapter", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(context.TurnIntent.CreativeBrief))
            args["creativeBrief"] = context.TurnIntent.CreativeBrief;

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
        task.Reason = "autopilot_resume";
        task.UpdatedAt = DateTime.UtcNow;
        plan.SchedulerState.ActiveTaskId = task.TaskId;
        plan.SchedulerState.ActiveRunId = task.RunId;
        plan.SchedulerState.ActiveChapterId = task.ChapterId;
        plan.SchedulerState.LastDecisionReason = task.Reason;
        plan.SchedulerState.UpdatedAt = DateTime.UtcNow;
    }

    private static bool ToolNeedsRunId(string toolName) =>
        toolName is "SelectChapterCandidate" or "BuildChapterContextPackage" or "GenerateChapterWithChanges" or
            "ValidateChapterDraft" or "RepairChapterDraft" or "CommitValidatedChapter" or "RefreshProjectIndexes" or
            "AnalyzeDependencyImpact" or "ReviewChapter" or "CommitVolumeArc" or "CommitStoryFoundation";
}

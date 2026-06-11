using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class MissionBlackboardRecoveryService
{
    private readonly AgentMissionTaskTreeService _taskTreeService;

    public MissionBlackboardRecoveryService(AgentMissionTaskTreeService taskTreeService)
    {
        _taskTreeService = taskTreeService;
    }

    public void Recover(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible)
    {
        var plan = session.WorkingMemory.MissionPlan ??= new AgentMissionPlan();
        var beforeRun = plan.CurrentRunId;
        var beforeTaskCount = plan.SchedulerState?.Tasks.Count ?? 0;

        session.NormalizeLegacyState();
        RestoreActiveRun(session, bible);
        _taskTreeService.Sync(session, project, bible);

        plan = session.WorkingMemory.MissionPlan;
        plan.LastRecoveredAt = DateTime.UtcNow;
        plan.LastRecoveredFrom = "StoryBible/runs/artifacts";
        plan.RecoveryConfidence = plan.SchedulerState.Tasks.Count > 0 || !string.IsNullOrWhiteSpace(plan.CurrentRunId) ? 0.86 : 0.55;
        plan.BlackboardVersion = Math.Max(plan.BlackboardVersion, 2);
        if (string.IsNullOrWhiteSpace(beforeRun) && !string.IsNullOrWhiteSpace(plan.CurrentRunId))
            AddWarning(plan, $"已从 StoryBible 恢复当前 Run：{plan.CurrentRunId}");
        if (beforeTaskCount == 0 && plan.SchedulerState.Tasks.Count > 0)
            AddWarning(plan, $"已从 StoryBible/artifacts 重建 {plan.SchedulerState.Tasks.Count} 个任务。");
    }

    private static void RestoreActiveRun(AgentSession session, StoryBibleDocument bible)
    {
        if (!string.IsNullOrWhiteSpace(session.ActiveRunId) && session.Phase != "idle")
            return;

        // Only restore runs that are genuinely awaiting confirmation (not completed/failed/cancelled)
        var awaiting = bible.AgentRuns
            .Where(r => r.Status == NovelAgentRunStatus.AwaitingConfirmation)
            .Where(r => AgentRunSelector.IsActionableAwaitingRun(bible, r))
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefault();

        if (awaiting == null)
            return;

        // Skip stale runs: if the run has been awaiting confirmation for more than 7 days, treat as stale
        if (awaiting.UpdatedAt < DateTime.UtcNow.AddDays(-7))
            return;

        session.ActiveRunId = awaiting.RunId;
        session.WorkingMemory.CurrentGoal = string.IsNullOrWhiteSpace(session.WorkingMemory.CurrentGoal)
            ? awaiting.UserGoal
            : session.WorkingMemory.CurrentGoal;
    }

    private static void AddWarning(AgentMissionPlan plan, string warning)
    {
        if (string.IsNullOrWhiteSpace(warning) || plan.RecoveryWarnings.Contains(warning))
            return;
        plan.RecoveryWarnings.Add(warning);
        if (plan.RecoveryWarnings.Count > 20)
            plan.RecoveryWarnings.RemoveRange(0, plan.RecoveryWarnings.Count - 20);
    }
}

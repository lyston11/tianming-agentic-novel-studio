using Microsoft.Extensions.Hosting;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentSchedulerHostedService : BackgroundService
{
    private readonly AgentSessionManager _sessionManager;
    private readonly NovelProjectCatalog _catalog;
    private readonly NovelAgentWorkspace _workspace;
    private readonly AgentMissionTaskTreeService _taskTreeService;
    private readonly AgentTaskScheduler _scheduler;
    private readonly AgentObservationBuilder _observationBuilder;
    private readonly ToolPolicyEngine _policyEngine;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly ReflectionEngine _reflectionEngine;

    public AgentSchedulerHostedService(
        AgentSessionManager sessionManager,
        NovelProjectCatalog catalog,
        NovelAgentWorkspace workspace,
        AgentMissionTaskTreeService taskTreeService,
        AgentTaskScheduler scheduler,
        AgentObservationBuilder observationBuilder,
        ToolPolicyEngine policyEngine,
        AgentToolRegistry toolRegistry,
        ReflectionEngine reflectionEngine)
    {
        _sessionManager = sessionManager;
        _catalog = catalog;
        _workspace = workspace;
        _taskTreeService = taskTreeService;
        _scheduler = scheduler;
        _observationBuilder = observationBuilder;
        _policyEngine = policyEngine;
        _toolRegistry = toolRegistry;
        _reflectionEngine = reflectionEngine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch
            {
                // Scheduler must never take the Web app down. Runtime turns still do strict execution.
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task TickAsync(CancellationToken ct)
    {
        foreach (var session in _sessionManager.ListSessions().Where(s => !s.IsArchived))
        {
            var project = await ResolveProjectAsync(session, ct).ConfigureAwait(false);
            StoryBibleDocument bible;
            try
            {
                bible = await _catalog.WithProjectAsync(project, () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            _taskTreeService.Sync(session, project, bible);
            NormalizeScheduler(session);
            await TryAdvanceOneSafeTaskAsync(session, project, bible, ct).ConfigureAwait(false);
            _sessionManager.SaveSession(session);
        }
    }

    private async Task<NovelProjectInfo> ResolveProjectAsync(AgentSession session, CancellationToken ct)
    {
        NovelProjectInfo? project = null;
        if (!string.IsNullOrWhiteSpace(session.ActiveProjectId))
            project = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
        project ??= await _catalog.GetActiveAsync(ct).ConfigureAwait(false);
        session.ActiveProjectId = project.Id;
        return project;
    }

    private void NormalizeScheduler(AgentSession session)
    {
        var plan = session.WorkingMemory.MissionPlan;
        plan.SchedulerState.LeaseOwner = "web-hosted-service";
        plan.SchedulerState.LeaseExpiresAt = DateTime.UtcNow.AddSeconds(45);
        plan.SchedulerLease = plan.SchedulerState.LeaseOwner;

        var active = _scheduler.SelectActiveTask(plan);
        if (active == null)
            return;

        if (active.RequiresConfirmation)
            active.RequiresConfirmation = false;

        if (active.Status == "waiting_confirmation")
        {
            active.Status = "queued";
            active.BlockedReason = string.Empty;
        }
        else if (active.Status == "running" && active.UpdatedAt < DateTime.UtcNow.AddMinutes(-5))
        {
            active.Status = "queued";
            active.BlockedReason = string.Empty;
        }

        plan.SchedulerState.ActiveTaskId = active.TaskId;
        plan.SchedulerState.ActiveRunId = active.RunId;
        plan.SchedulerState.ActiveChapterId = active.ChapterId;
        plan.SchedulerState.LastDecisionReason = active.Reason;
        plan.SchedulerState.UpdatedAt = DateTime.UtcNow;
        plan.LastUserVisibleState = BuildVisibleState(active);
        plan.UpdatedAt = DateTime.UtcNow;
    }

    private async Task TryAdvanceOneSafeTaskAsync(
        AgentSession session,
        NovelProjectInfo project,
        StoryBibleDocument bible,
        CancellationToken ct)
    {
        var plan = session.WorkingMemory.MissionPlan;
        var active = _scheduler.SelectActiveTask(plan);
        if (active == null ||
            active.Status != "queued" ||
            string.IsNullOrWhiteSpace(active.NextAction))
            return;

        var intent = new TurnIntent
        {
            Type = TurnIntentType.ContinueMission,
            Label = "continue_mission",
            RawMessage = "后台调度继续任务",
            Source = "agent_scheduler_hosted_service",
        };
        var context = await _catalog.WithProjectAsync(project,
            () => _observationBuilder.BuildAsync(session, project, bible, intent.RawMessage, intent, ct), ct)
            .ConfigureAwait(false);
        var action = _scheduler.BuildContinueAction(context);
        if (action?.ToolCall == null)
            return;

        action.RequiresConfirmation = false;
        var policy = _policyEngine.BeforeCall(action.ToolCall, session, bible, context, confirmed: true);
        active.Reason = policy.Message;
        if (!policy.IsRepairable && policy.ReplacementAction?.ToolCall != null)
            action = policy.ReplacementAction;
        else if (!policy.AllowsExecution)
        {
            active.Status = "blocked";
            active.BlockedReason = string.IsNullOrWhiteSpace(policy.UserFacingMessage)
                ? string.IsNullOrWhiteSpace(policy.Message) ? "工具策略阻止后台执行。" : policy.Message
                : policy.UserFacingMessage;
            if (policy.IsRepairable && !string.IsNullOrWhiteSpace(policy.RecommendedToolName))
            {
                active.NextAction = policy.RecommendedToolName;
                active.RequiresConfirmation = false;
                active.Risk = policy.RecommendedToolName is "GenerateChapterWithChanges" or "RepairChapterDraft" or
                    "CommitValidatedChapter" or "CommitStoryFoundation" or "CommitVolumeArc"
                    ? "High"
                    : "Medium";
            }
            active.UpdatedAt = DateTime.UtcNow;
            return;
        }

        active.Status = "running";
        active.UpdatedAt = DateTime.UtcNow;
        plan.SchedulerState.ActiveTaskId = active.TaskId;
        plan.SchedulerState.UpdatedAt = DateTime.UtcNow;

        var result = await _catalog.WithProjectAsync(project,
            () => _toolRegistry.ExecuteAsync(action.ToolCall, session, bible, confirmed: true, ct), ct)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(result.Phase))
            session.Phase = result.Phase;
        if (!string.IsNullOrWhiteSpace(result.RunId))
            session.ActiveRunId = result.RunId;

        var observation = AddSchedulerObservation(session, action.ToolCall.Name, result);
        if (!result.Success || result.RequiresConfirmation)
        {
            active.Status = "blocked";
            active.BlockedReason = result.Message;
            active.UpdatedAt = DateTime.UtcNow;
            _taskTreeService.Sync(session, project, bible, result, action);
            return;
        }

        var refreshedBible = await _catalog.WithProjectAsync(project,
            () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct)
            .ConfigureAwait(false);
        var reflectContext = await _catalog.WithProjectAsync(project,
            () => _observationBuilder.BuildAsync(session, project, refreshedBible, intent.RawMessage, intent, ct), ct)
            .ConfigureAwait(false);
        var reflection = await _reflectionEngine.ReflectAsync(reflectContext, observation, ct).ConfigureAwait(false);
        _taskTreeService.Sync(session, project, refreshedBible, result, action, reflection);
        active.Status = reflection.RequiresUserInput ? "blocked" :
            reflection.QualityGate.Status is "fail" or "needs_rewrite" ? "blocked" : "done";
        active.BlockedReason = reflection.RequiresUserInput || active.Status == "blocked"
            ? FirstNonEmpty(reflection.QualityGate.RewriteDecision, reflection.Summary, result.Message)
            : string.Empty;
        active.UpdatedAt = DateTime.UtcNow;
    }

    private static AgentRuntimeObservation AddSchedulerObservation(
        AgentSession session,
        string toolName,
        AgentToolExecutionResult result)
    {
        var observation = new AgentRuntimeObservation
        {
            StepIndex = 0,
            ToolName = toolName,
            Success = result.Success,
            RequiresConfirmation = result.RequiresConfirmation,
            Risk = result.Risk,
            Message = result.Message.Length <= 500 ? result.Message : result.Message[..500],
            RunId = result.RunId ?? session.ActiveRunId ?? string.Empty,
            Phase = string.IsNullOrWhiteSpace(result.Phase) ? session.Phase : result.Phase,
            Artifact = result.Artifact,
        };
        session.WorkingMemory.RecentObservations.Add(observation);
        if (session.WorkingMemory.RecentObservations.Count > 16)
            session.WorkingMemory.RecentObservations.RemoveRange(0, session.WorkingMemory.RecentObservations.Count - 16);
        return observation;
    }

    private static string BuildVisibleState(AgentScheduledTask task)
    {
        var status = task.Status switch
        {
            "blocked" => "已阻塞",
            "running" => "执行中",
            "done" => "已完成",
            _ => "排队中",
        };
        return $"{status}：{task.ProjectTitle}/{task.ChapterId}/{task.NextAction}";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

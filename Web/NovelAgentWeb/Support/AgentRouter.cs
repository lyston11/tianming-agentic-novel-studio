using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentRouter
{
    private readonly AgentSessionManager _sessions;
    private readonly IAgentToolExecutionLedger _toolExecutionLedger;
    private readonly ICurrentUserService _currentUser;
    private readonly IAgentRuntimeRunService _runtimeRuns;
    private readonly IAgentInterruptService _interrupts;
    private readonly IAgentRuntimeQueue _queue;
    private readonly IAgentForegroundTurnRunner _foreground;

    public AgentRouter(
        AgentSessionManager sessions,
        IAgentToolExecutionLedger toolExecutionLedger,
        ICurrentUserService currentUser,
        IAgentRuntimeRunService runtimeRuns,
        IAgentInterruptService interrupts,
        IAgentRuntimeQueue queue,
        IAgentForegroundTurnRunner foreground)
    {
        _sessions = sessions;
        _toolExecutionLedger = toolExecutionLedger;
        _currentUser = currentUser;
        _runtimeRuns = runtimeRuns;
        _interrupts = interrupts;
        _queue = queue;
        _foreground = foreground;
    }

    public async Task<AgentChatResponse> HandleAsync(
        string sessionId,
        string userMessage,
        CancellationToken ct)
    {
        var session = await _sessions.GetOrCreateSessionAsync(sessionId, ct).ConfigureAwait(false);
        var userId = _currentUser.GetUserId();
        var activeRun = await _runtimeRuns.TryGetActiveAsync(userId, session.SessionId, ct).ConfigureAwait(false);
        if (activeRun != null)
            return await RecordInterruptAsync(session, activeRun, userMessage, ct).ConfigureAwait(false);

        var foreground = await _foreground.TryHandleAsync(session.SessionId, userMessage, ct).ConfigureAwait(false);
        if (foreground.Response != null)
            return foreground.Response;
        if (!foreground.StartBackground)
        {
            return new AgentChatResponse(
                "这一轮没有启动后台任务。你可以直接追问、补充要求，或让我开始一个明确的创作执行。",
                Array.Empty<string>(),
                session.SessionId,
                null,
                "idle",
                Memory: AgentWorkingMemorySnapshot.From(session.WorkingMemory));
        }

        var runtimeRun = await _runtimeRuns.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
                userId,
                session.SessionId,
                string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
                userMessage),
            ct).ConfigureAwait(false);
        await _queue.EnqueueAsync(runtimeRun.Id, ct).ConfigureAwait(false);

        return new AgentChatResponse(
            "已开始在后台执行。我会通过实时进度告诉你现在做到哪一步；执行中你也可以继续追问进度、补充要求或要求暂停。",
            new[] { "查看执行进度", "补充要求", "暂停调整" },
            session.SessionId,
            runtimeRun.Id,
            "queued");
    }

    private async Task<AgentChatResponse> RecordInterruptAsync(
        AgentSession session,
        Data.Entities.AgentRuntimeRun activeRun,
        string userMessage,
        CancellationToken ct)
    {
        await _interrupts.AddAsync(new CreateAgentInterruptRequest(
                activeRun.Id,
                activeRun.UserId,
                activeRun.SessionId,
                activeRun.ProjectId,
                "freeform",
                userMessage,
                0),
            ct).ConfigureAwait(false);

        var progressReply = await BuildActiveRunProgressReplyAsync(session, activeRun, ct).ConfigureAwait(false);
        return new AgentChatResponse(
            $"我收到你的消息了。\n{progressReply}",
            new[] { "查看执行进度", "继续补充要求", "暂停调整" },
            session.SessionId,
            activeRun.Id,
            "interrupt_queued");
    }

    private async Task<string> BuildActiveRunProgressReplyAsync(
        AgentSession session,
        Data.Entities.AgentRuntimeRun activeRun,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(activeRun.ActiveTool))
        {
            var toolProgress = AgentToolProgressPresenter.DescribeRunning(
                activeRun.ActiveTool,
                activeRun.Status,
                activeRun.CurrentPhase,
                activeRun.Id);
            return $"当前状态：{toolProgress.Title}。{toolProgress.Detail}";
        }

        if (!string.IsNullOrWhiteSpace(activeRun.LastMessage) && IsUserSafeRuntimeMessage(activeRun.LastMessage))
            return $"当前状态：{activeRun.LastMessage}";

        var recent = await _toolExecutionLedger
            .GetRecentAsync(
                session.UserId,
                session.SessionId,
                string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
                ct)
            .ConfigureAwait(false);
        var running = recent.FirstOrDefault(x => string.Equals(x.Status, "running", StringComparison.OrdinalIgnoreCase));
        if (running != null)
        {
            var progress = AgentToolProgressPresenter.Describe(running);
            return $"当前状态：{progress.Title}。{progress.Detail}";
        }

        return $"当前状态：{RuntimeStatusLabel(activeRun.Status)}。";
    }

    private static bool IsUserSafeRuntimeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var text = message.Trim();
        var internalTokens = new[]
        {
            "tool_search",
            "ProcessKnowledgeFile",
            "PlanStoryFoundation",
            "CommitStoryFoundation",
            "QueryWorkspaceState",
            "QueryProjectStatus",
            "SearchCreativeKnowledge",
            "ResolveNovelProject",
            "foundation_candidates",
            "volume_arc_candidates",
            "chapter_candidates",
            "interrupt_received",
            "mission_updated",
            "step_complete",
            "step_failed"
        };
        return !internalTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string RuntimeStatusLabel(string status) =>
        status switch
        {
            AgentRuntimeRunStatus.Queued => "排队等待执行",
            AgentRuntimeRunStatus.Running => "后台执行中",
            AgentRuntimeRunStatus.Completed => "已完成",
            AgentRuntimeRunStatus.Failed => "执行失败",
            AgentRuntimeRunStatus.Cancelled => "已取消",
            _ => "后台处理中"
        };

}

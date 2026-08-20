using System.Text.Json;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Context;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class TargetArchitectureDirector : IAgentForegroundTurnRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAgentSessionApplicationService _sessions;
    private readonly ICurrentUserService _currentUser;
    private readonly IChatHistoryRepository _chatHistory;
    private readonly ICollaborationMemoryService _collaborationMemory;
    private readonly ICommitmentAssessmentService _commitments;
    private readonly IAgentContextAssembler _contexts;

    public TargetArchitectureDirector(
        IAgentSessionApplicationService sessions,
        ICurrentUserService currentUser,
        IChatHistoryRepository chatHistory,
        ICollaborationMemoryService collaborationMemory,
        ICommitmentAssessmentService commitments,
        IAgentContextAssembler contexts)
    {
        _sessions = sessions;
        _currentUser = currentUser;
        _chatHistory = chatHistory;
        _collaborationMemory = collaborationMemory;
        _commitments = commitments;
        _contexts = contexts;
    }

    public async Task<AgentForegroundTurnResult> TryHandleAsync(
        string sessionId,
        string userMessage,
        string? canonicalMessageKey,
        CancellationToken ct)
    {
        var message = RequireText(userMessage, nameof(userMessage));
        var userId = _currentUser.GetUserId();
        var session = await _sessions.GetRuntimeSessionAsync(sessionId, ct).ConfigureAwait(false);
        var projectId = session.ActiveProjectId;

        await _chatHistory.AppendAsync(
            userId,
            string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            session.SessionId,
            "user",
            message,
            ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(projectId))
        {
            return await ReplyAsync(
                session,
                "当前会话还没有绑定小说项目。请先选择项目，再继续讨论创作目标。",
                ["选择项目"],
                "project_required",
                null,
                null,
                ct).ConfigureAwait(false);
        }

        var context = await _contexts.BuildAsync(new AgentContextRequest(
                AgentContextProfile.Conversation,
                userId,
                projectId,
                session.SessionId,
                message), ct)
            .ConfigureAwait(false);

        var assessment = await _commitments.AssessAsync(new CommitmentAssessmentRequest(
            projectId,
            context.LatestGoal?.CollaborationMode ?? "coauthor",
            context.Dialogue,
            JsonSerializer.Serialize(context.Memory.Records, JsonOptions),
            JsonSerializer.Serialize(new
            {
                context.Project,
                context.LatestGoal,
                StructuredMemory = context.Memory.Structured,
                context.Knowledge,
                context.PendingIntents,
                context.Sources
            }, JsonOptions),
            ExplicitExecutionAction: false,
            ProposedContract: null), ct).ConfigureAwait(false);

        await _collaborationMemory.AddSessionStateAsync(
            userId,
            projectId,
            session.SessionId,
            CollaborationMemoryKind.CommitmentJudgment,
            JsonSerializer.Serialize(assessment, JsonOptions),
            accepted: assessment.State == DialogueCommitmentState.Committed && !assessment.RequiresConfirmation,
            ct).ConfigureAwait(false);

        var director = new DirectorTurnView(
            projectId,
            assessment.State,
            assessment.Authorization,
            assessment.RequiresConfirmation,
            assessment.Rationale,
            assessment.ProposedContract);
        return await ReplyAsync(
            session,
            BuildReply(assessment),
            BuildSuggestions(assessment),
            Phase(assessment.State),
            director,
            context.Knowledge,
            ct).ConfigureAwait(false);
    }

    private async Task<AgentForegroundTurnResult> ReplyAsync(
        AgentSession session,
        string reply,
        IReadOnlyList<string> suggestions,
        string phase,
        DirectorTurnView? director,
        AgentKnowledgeContext? knowledge,
        CancellationToken ct)
    {
        await _chatHistory.AppendAsync(
            session.UserId,
            string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
            session.SessionId,
            "assistant",
            reply,
            ct,
            knowledge).ConfigureAwait(false);
        session.Phase = phase;
        await _sessions.SaveRuntimeSessionAsync(session, ct).ConfigureAwait(false);
        return AgentForegroundTurnResult.Reply(new AgentChatResponse(
            reply,
            suggestions,
            session.SessionId,
            Phase: phase,
            ActiveProjectId: session.ActiveProjectId,
            Director: director,
            Knowledge: knowledge));
    }

    private static string BuildReply(CommitmentAssessment assessment)
    {
        if (assessment.ProposedContract == null)
            return assessment.Rationale;

        var contract = assessment.ProposedContract;
        var confirmation = assessment.RequiresConfirmation || assessment.State != DialogueCommitmentState.Committed
            ? "这仍是可修改的目标提案，不会自动启动生产。"
            : "目标合同已经明确，请核对金额上限后通过执行控件授权。";
        return $"{assessment.Rationale}\n\n目标：{contract.HumanReadableObjective}\n整书章节范围：{contract.TargetChapterRangeJson}\n执行策略：{contract.ExecutionStrategy}\n协作模式：{contract.CollaborationMode}\n{confirmation}";
    }

    private static IReadOnlyList<string> BuildSuggestions(CommitmentAssessment assessment) =>
        assessment.ProposedContract == null
            ? ["继续讨论", "补充约束"]
            : ["修改目标", "核对合同", "设置金额上限"];

    private static string Phase(DialogueCommitmentState state) => state switch
    {
        DialogueCommitmentState.Exploring => "goal_exploring",
        DialogueCommitmentState.Proposed => "goal_proposed",
        DialogueCommitmentState.Committed => "goal_ready_for_confirmation",
        DialogueCommitmentState.Revising => "goal_revising",
        DialogueCommitmentState.Cancelled => "goal_cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("对话内容不能为空。", parameterName)
            : value.Trim();
}

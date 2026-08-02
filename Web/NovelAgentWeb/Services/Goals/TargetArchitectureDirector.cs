using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class TargetArchitectureDirector : IAgentForegroundTurnRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AgentSessionManager _sessions;
    private readonly ICurrentUserService _currentUser;
    private readonly IChatHistoryRepository _chatHistory;
    private readonly ICollaborationMemoryService _collaborationMemory;
    private readonly ICommitmentAssessmentService _commitments;
    private readonly IKnowledgeQueryTool _knowledgeQuery;
    private readonly NovelAgentDbContext _db;

    public TargetArchitectureDirector(
        AgentSessionManager sessions,
        ICurrentUserService currentUser,
        IChatHistoryRepository chatHistory,
        ICollaborationMemoryService collaborationMemory,
        ICommitmentAssessmentService commitments,
        IKnowledgeQueryTool knowledgeQuery,
        NovelAgentDbContext db)
    {
        _sessions = sessions;
        _currentUser = currentUser;
        _chatHistory = chatHistory;
        _collaborationMemory = collaborationMemory;
        _commitments = commitments;
        _knowledgeQuery = knowledgeQuery;
        _db = db;
    }

    public async Task<AgentForegroundTurnResult> TryHandleAsync(
        string sessionId,
        string userMessage,
        CancellationToken ct)
    {
        var message = RequireText(userMessage, nameof(userMessage));
        var userId = _currentUser.GetUserId();
        var session = await _sessions.GetOrCreateSessionAsync(sessionId, ct).ConfigureAwait(false);
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

        var ownsProject = await _db.NovelProjects.AsNoTracking().AnyAsync(
            item => item.Id == projectId && item.UserId == userId,
            ct).ConfigureAwait(false);
        if (!ownsProject)
            throw new KeyNotFoundException("项目不存在或不属于当前用户。");

        var knowledge = await _knowledgeQuery.ExecuteAsync(new KnowledgeQueryRequest(
                KnowledgeQueryIntent.Retrieve,
                KnowledgeQueryScope.CurrentProject,
                projectId,
                message,
                Limit: 8), ct)
            .ConfigureAwait(false);

        var dialogue = await BuildDialogueAsync(userId, projectId, session.SessionId, ct).ConfigureAwait(false);
        var decisions = await _db.ProjectCollaborationDecisions.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId && item.Status == "active")
            .OrderBy(item => item.CreatedAt)
            .Select(item => new { item.Id, item.MemoryKind, item.ContentJson, item.Scope, item.EffectiveGoalId })
            .ToArrayAsync(ct).ConfigureAwait(false);
        var project = await _db.NovelProjects.AsNoTracking()
            .Where(item => item.Id == projectId && item.UserId == userId)
            .Select(item => new { item.Id, item.Title, item.Genre, item.SubGenre, item.CoreHook, item.Status, item.WordCount })
            .SingleAsync(ct).ConfigureAwait(false);
        var chapterCount = await _db.Chapters.AsNoTracking()
            .CountAsync(item => item.ProjectId == projectId, ct).ConfigureAwait(false);
        var latestGoal = await _db.CreativeGoals.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new { item.Id, item.Status, item.CollaborationMode, item.HumanReadableObjective })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var assessment = await _commitments.AssessAsync(new CommitmentAssessmentRequest(
            projectId,
            latestGoal?.CollaborationMode ?? "coauthor",
            dialogue,
            JsonSerializer.Serialize(decisions, JsonOptions),
            JsonSerializer.Serialize(new
            {
                Project = project,
                ChapterCount = chapterCount,
                LatestGoal = latestGoal,
                Knowledge = knowledge.Context
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
            knowledge.Context,
            ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DialogueMessage>> BuildDialogueAsync(
        string userId,
        string projectId,
        string sessionId,
        CancellationToken ct)
    {
        var window = await _chatHistory.GetPromptWindowAsync(userId, projectId, sessionId, ct)
            .ConfigureAwait(false);
        var messages = new List<DialogueMessage>();
        if (!string.IsNullOrWhiteSpace(window.MetaSummary))
            messages.Add(new DialogueMessage("context", window.MetaSummary));
        messages.AddRange(window.Summaries.Select(item => new DialogueMessage("context", item.Content)));
        messages.AddRange(window.RecentMessages.Select(item => new DialogueMessage(item.Role, item.Content)));
        return messages;
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
        await _sessions.SaveSessionAsync(session, ct).ConfigureAwait(false);
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

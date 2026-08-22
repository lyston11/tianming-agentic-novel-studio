using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using AuthorMemoryEntity = TM.Web.NovelAgentWeb.Data.Entities.AuthorMemory;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public enum CollaborationMemoryKind
{
    WorkFact,
    AuthorPreference,
    CollaborationHabit,
    Taboo,
    AbstractStylePreference,
    CreativeDecision,
    CollaborationMode,
    AestheticDirection,
    OpenCreativeQuestion,
    DialogueProposal,
    DialogueReference,
    CommitmentJudgment,
    ReworkIntent
}

public enum ExperienceSuggestionAction
{
    Accept,
    Reject,
    TryOnce
}

public sealed record ExperienceSuggestionDecisionResult(
    ExperienceSuggestion Suggestion,
    ProjectCollaborationDecision? Decision);

public sealed record WorkingContextRequest(
    string UserId,
    string ProjectId,
    string SessionId,
    string GoalId,
    string CanonVersion,
    string GoalContractJson,
    string KnowledgeSnapshotVersion,
    IReadOnlyList<string> CanonEvidenceIds,
    IReadOnlyList<string> KnowledgeEntryIds);

public sealed record CollaborationDecisionSnapshot(
    string Id,
    CollaborationMemoryKind MemoryKind,
    string ContentJson,
    string Scope,
    string? EffectiveGoalId);

public sealed record SessionDialogueSnapshot(
    string Id,
    CollaborationMemoryKind MemoryKind,
    string ContentJson,
    string Status);

public sealed record ExperienceSuggestionSnapshot(
    string Id,
    string SuggestionType,
    string ProposedChangeJson,
    string Rationale);

public sealed record WorkingContext(
    string UserId,
    string ProjectId,
    string SessionId,
    string GoalId,
    string CanonVersion,
    string GoalContractJson,
    string KnowledgeSnapshotVersion,
    IReadOnlyList<string> CanonEvidenceIds,
    IReadOnlyList<string> KnowledgeEntryIds,
    IReadOnlyList<CollaborationDecisionSnapshot> ProjectDecisions,
    IReadOnlyList<SessionDialogueSnapshot> SessionState,
    IReadOnlyList<ExperienceSuggestionSnapshot> AdvisorySuggestions);

public interface ICollaborationMemoryService
{
    Task<AuthorMemoryEntity> AddAuthorMemoryAsync(
        string userId,
        CollaborationMemoryKind kind,
        string contentJson,
        string source,
        CancellationToken cancellationToken = default);

    Task<ProjectCollaborationDecision> AddProjectDecisionAsync(
        string userId,
        string projectId,
        CollaborationMemoryKind kind,
        string contentJson,
        string source,
        CancellationToken cancellationToken = default);

    Task<SessionDialogueState> AddSessionStateAsync(
        string userId,
        string projectId,
        string sessionId,
        CollaborationMemoryKind kind,
        string contentJson,
        bool accepted,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionDialogueState>> GetSessionStateAsync(
        string userId,
        string projectId,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<ProjectCollaborationDecision> AcceptSessionProposalAsync(
        string userId,
        string projectId,
        string sessionId,
        string sessionStateId,
        CollaborationMemoryKind projectKind,
        CancellationToken cancellationToken = default);

    Task<ExperienceObservation> AddExperienceObservationAsync(
        string userId,
        string projectId,
        string? goalId,
        string observationType,
        string evidenceJson,
        string metricsJson,
        CancellationToken cancellationToken = default);

    Task<ExperienceSuggestion> AddExperienceSuggestionAsync(
        string userId,
        string projectId,
        string observationId,
        string suggestionType,
        string proposedChangeJson,
        string rationale,
        string suppressionFingerprint,
        CancellationToken cancellationToken = default);

    Task<ExperienceSuggestionDecisionResult> DecideSuggestionAsync(
        string userId,
        string projectId,
        string suggestionId,
        ExperienceSuggestionAction action,
        string? nextGoalId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<WorkingContext> CompileWorkingContextAsync(
        WorkingContextRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CollaborationMemoryService : ICollaborationMemoryService
{
    private static readonly HashSet<CollaborationMemoryKind> AuthorKinds =
    [
        CollaborationMemoryKind.AuthorPreference,
        CollaborationMemoryKind.CollaborationHabit,
        CollaborationMemoryKind.Taboo,
        CollaborationMemoryKind.AbstractStylePreference
    ];

    private static readonly HashSet<CollaborationMemoryKind> ProjectKinds =
    [
        CollaborationMemoryKind.CreativeDecision,
        CollaborationMemoryKind.CollaborationMode,
        CollaborationMemoryKind.AestheticDirection,
        CollaborationMemoryKind.OpenCreativeQuestion
    ];

    private static readonly HashSet<CollaborationMemoryKind> SessionKinds =
    [
        CollaborationMemoryKind.DialogueProposal,
        CollaborationMemoryKind.DialogueReference,
        CollaborationMemoryKind.CommitmentJudgment,
        CollaborationMemoryKind.ReworkIntent
    ];

    private readonly NovelAgentDbContext _db;

    public CollaborationMemoryService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<AuthorMemoryEntity> AddAuthorMemoryAsync(
        string userId,
        CollaborationMemoryKind kind,
        string contentJson,
        string source,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(AuthorKinds, kind, "Author Memory");
        ValidateJson(contentJson);
        var nextVersion = (await _db.AuthorMemories.AsNoTracking()
            .Where(item => item.UserId == userId && item.MemoryKind == kind.ToString())
            .MaxAsync(item => (int?)item.Version, cancellationToken) ?? 0) + 1;
        var memory = new AuthorMemoryEntity
        {
            UserId = Require(userId),
            MemoryKind = kind.ToString(),
            ContentJson = contentJson,
            Source = Require(source),
            Version = nextVersion
        };
        _db.AuthorMemories.Add(memory);
        await _db.SaveChangesAsync(cancellationToken);
        return memory;
    }

    public async Task<ProjectCollaborationDecision> AddProjectDecisionAsync(
        string userId,
        string projectId,
        CollaborationMemoryKind kind,
        string contentJson,
        string source,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(ProjectKinds, kind, "Project Collaboration Memory");
        ValidateJson(contentJson);
        var decision = NewDecision(userId, projectId, kind, contentJson, source);
        _db.ProjectCollaborationDecisions.Add(decision);
        await _db.SaveChangesAsync(cancellationToken);
        return decision;
    }

    public async Task<SessionDialogueState> AddSessionStateAsync(
        string userId,
        string projectId,
        string sessionId,
        CollaborationMemoryKind kind,
        string contentJson,
        bool accepted,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(SessionKinds, kind, "Session Dialogue State");
        ValidateJson(contentJson);
        var state = new SessionDialogueState
        {
            UserId = Require(userId),
            ProjectId = Require(projectId),
            SessionId = Require(sessionId),
            MemoryKind = kind.ToString(),
            ContentJson = contentJson,
            Status = accepted ? "accepted" : "pending"
        };
        _db.SessionDialogueStates.Add(state);
        await _db.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<IReadOnlyList<SessionDialogueState>> GetSessionStateAsync(
        string userId,
        string projectId,
        string sessionId,
        CancellationToken cancellationToken = default) =>
        await _db.SessionDialogueStates.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId && item.SessionId == sessionId)
            .OrderBy(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<ProjectCollaborationDecision> AcceptSessionProposalAsync(
        string userId,
        string projectId,
        string sessionId,
        string sessionStateId,
        CollaborationMemoryKind projectKind,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(ProjectKinds, projectKind, "Project Collaboration Memory");
        var state = await _db.SessionDialogueStates.SingleOrDefaultAsync(item =>
            item.Id == sessionStateId && item.UserId == userId &&
            item.ProjectId == projectId && item.SessionId == sessionId,
            cancellationToken) ?? throw new KeyNotFoundException("会话提案不存在或不属于当前会话。");
        if (state.Status != "pending")
            throw new InvalidOperationException("只有待确认会话提案可以被接受。");

        state.Status = "accepted";
        state.UpdatedAt = DateTime.UtcNow;
        var decision = NewDecision(userId, projectId, projectKind, state.ContentJson, "accepted_session_proposal");
        decision.SourceSessionStateId = state.Id;
        _db.ProjectCollaborationDecisions.Add(decision);
        await _db.SaveChangesAsync(cancellationToken);
        return decision;
    }

    public async Task<ExperienceObservation> AddExperienceObservationAsync(
        string userId,
        string projectId,
        string? goalId,
        string observationType,
        string evidenceJson,
        string metricsJson,
        CancellationToken cancellationToken = default)
    {
        ValidateJson(evidenceJson);
        ValidateJson(metricsJson);
        var observation = new ExperienceObservation
        {
            UserId = Require(userId),
            ProjectId = Require(projectId),
            GoalId = goalId,
            ObservationType = Require(observationType),
            EvidenceJson = evidenceJson,
            MetricsJson = metricsJson
        };
        _db.ExperienceObservations.Add(observation);
        await _db.SaveChangesAsync(cancellationToken);
        return observation;
    }

    public async Task<ExperienceSuggestion> AddExperienceSuggestionAsync(
        string userId,
        string projectId,
        string observationId,
        string suggestionType,
        string proposedChangeJson,
        string rationale,
        string suppressionFingerprint,
        CancellationToken cancellationToken = default)
    {
        ValidateJson(proposedChangeJson);
        var ownsObservation = await _db.ExperienceObservations.AsNoTracking().AnyAsync(item =>
            item.Id == observationId && item.UserId == userId && item.ProjectId == projectId,
            cancellationToken);
        if (!ownsObservation)
            throw new KeyNotFoundException("经验观察不存在或不属于当前项目。");
        var fingerprint = Require(suppressionFingerprint);
        var suppressed = await _db.ExperienceSuggestions.AsNoTracking().AnyAsync(item =>
            item.UserId == userId && item.ProjectId == projectId &&
            item.SuppressionFingerprint == fingerprint && item.Status == "rejected",
            cancellationToken);
        if (suppressed)
            throw new InvalidOperationException("该经验建议已被用户拒绝，当前项目中不再重复提出。");
        var suggestion = new ExperienceSuggestion
        {
            UserId = Require(userId),
            ProjectId = Require(projectId),
            ObservationId = Require(observationId),
            SuggestionType = Require(suggestionType),
            ProposedChangeJson = proposedChangeJson,
            Rationale = Require(rationale),
            SuppressionFingerprint = fingerprint
        };
        _db.ExperienceSuggestions.Add(suggestion);
        await _db.SaveChangesAsync(cancellationToken);
        return suggestion;
    }

    public async Task<ExperienceSuggestionDecisionResult> DecideSuggestionAsync(
        string userId,
        string projectId,
        string suggestionId,
        ExperienceSuggestionAction action,
        string? nextGoalId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var suggestion = await _db.ExperienceSuggestions.SingleOrDefaultAsync(item =>
            item.Id == suggestionId && item.UserId == userId && item.ProjectId == projectId,
            cancellationToken) ?? throw new KeyNotFoundException("经验建议不存在或不属于当前项目。");
        if (suggestion.Status != "pending")
            throw new InvalidOperationException("经验建议已经做出决定。");
        if (action == ExperienceSuggestionAction.TryOnce)
            nextGoalId = Require(nextGoalId);
        else if (!string.IsNullOrWhiteSpace(nextGoalId))
            throw new InvalidOperationException("只有 TryOnce 可以绑定 Goal。");

        suggestion.Status = action switch
        {
            ExperienceSuggestionAction.Accept => "accepted",
            ExperienceSuggestionAction.Reject => "rejected",
            ExperienceSuggestionAction.TryOnce => "try_once",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        suggestion.DecisionReason = Require(reason);
        suggestion.EffectiveGoalId = nextGoalId;
        suggestion.DecidedAt = DateTime.UtcNow;
        suggestion.UpdatedAt = suggestion.DecidedAt.Value;
        suggestion.DecisionVersion++;

        ProjectCollaborationDecision? decision = null;
        if (action != ExperienceSuggestionAction.Reject)
        {
            decision = NewDecision(
                userId,
                projectId,
                CollaborationMemoryKind.CreativeDecision,
                suggestion.ProposedChangeJson,
                "experience_suggestion");
            decision.SourceSuggestionId = suggestion.Id;
            if (action == ExperienceSuggestionAction.TryOnce)
            {
                decision.Scope = "goal";
                decision.EffectiveGoalId = nextGoalId;
                decision.ExpiresAfterGoal = true;
            }
            _db.ProjectCollaborationDecisions.Add(decision);
        }
        await _db.SaveChangesAsync(cancellationToken);
        return new ExperienceSuggestionDecisionResult(suggestion, decision);
    }

    public async Task<WorkingContext> CompileWorkingContextAsync(
        WorkingContextRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(request.UserId);
        Require(request.ProjectId);
        Require(request.SessionId);
        Require(request.GoalId);
        ValidateJson(request.GoalContractJson);

        var decisionRows = await _db.ProjectCollaborationDecisions.AsNoTracking()
            .Where(item => item.UserId == request.UserId && item.ProjectId == request.ProjectId &&
                item.Status == "active" &&
                (item.Scope == "project" || (item.Scope == "goal" && item.EffectiveGoalId == request.GoalId)))
            .OrderBy(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var decisions = decisionRows.Select(item => new CollaborationDecisionSnapshot(
                item.Id,
                Enum.Parse<CollaborationMemoryKind>(item.MemoryKind),
                item.ContentJson,
                item.Scope,
                item.EffectiveGoalId))
            .ToArray();
        var sessionRows = await _db.SessionDialogueStates.AsNoTracking()
            .Where(item => item.UserId == request.UserId && item.ProjectId == request.ProjectId &&
                item.SessionId == request.SessionId && (item.Status == "pending" || item.Status == "accepted"))
            .OrderBy(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var sessionState = sessionRows.Select(item => new SessionDialogueSnapshot(
                item.Id,
                Enum.Parse<CollaborationMemoryKind>(item.MemoryKind),
                item.ContentJson,
                item.Status))
            .ToArray();
        var suggestions = await _db.ExperienceSuggestions.AsNoTracking()
            .Where(item => item.UserId == request.UserId && item.ProjectId == request.ProjectId && item.Status == "pending")
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new ExperienceSuggestionSnapshot(
                item.Id,
                item.SuggestionType,
                item.ProposedChangeJson,
                item.Rationale))
            .ToArrayAsync(cancellationToken);

        return new WorkingContext(
            request.UserId,
            request.ProjectId,
            request.SessionId,
            request.GoalId,
            Require(request.CanonVersion),
            request.GoalContractJson,
            Require(request.KnowledgeSnapshotVersion),
            request.CanonEvidenceIds.ToArray(),
            request.KnowledgeEntryIds.ToArray(),
            decisions,
            sessionState,
            suggestions);
    }

    private static ProjectCollaborationDecision NewDecision(
        string userId,
        string projectId,
        CollaborationMemoryKind kind,
        string contentJson,
        string source) => new()
        {
            UserId = Require(userId),
            ProjectId = Require(projectId),
            MemoryKind = kind.ToString(),
            ContentJson = contentJson,
            Source = Require(source)
        };

    private static void ValidateScope(
        IReadOnlySet<CollaborationMemoryKind> allowed,
        CollaborationMemoryKind kind,
        string scope)
    {
        if (kind == CollaborationMemoryKind.WorkFact)
            throw new InvalidOperationException("作品事实必须进入正史或知识真源，不能写入协作记忆。");
        if (!allowed.Contains(kind))
            throw new InvalidOperationException($"{kind} 不属于 {scope}。");
    }

    private static void ValidateJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var _ = JsonDocument.Parse(json);
    }

    private static string Require(string? value) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException("必填值不能为空。", nameof(value));
}

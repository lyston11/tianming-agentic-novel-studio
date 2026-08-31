using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Rework;

public interface IReworkIntentService
{
    Task<ReworkIntent> CompileAsync(
        CompileReworkIntentRequest request,
        CancellationToken cancellationToken = default);

    Task<ReworkIntent> StartAutomaticAttemptAsync(
        string intentId,
        CancellationToken cancellationToken = default);

    Task<ReworkIntent> RecordAttemptOutcomeAsync(
        string intentId,
        bool problemResolved,
        bool problemImproved,
        bool impactExpanded,
        CancellationToken cancellationToken = default);
}

public sealed class ReworkIntentService : IReworkIntentService
{
    private static readonly HashSet<string> TargetScopes = ["selection", "chapter"];
    private static readonly HashSet<string> ImpactLevels = ["copy", "local_fact", "key_plot", "hard_conflict"];
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IReworkIntentModelClient _model;
    private readonly ReworkBudgetPolicy _budget;

    public ReworkIntentService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        IReworkIntentModelClient model,
        ReworkBudgetPolicy budget)
    {
        _db = db;
        _currentUser = currentUser;
        _model = model;
        _budget = budget;
    }

    public async Task<ReworkIntent> CompileAsync(
        CompileReworkIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserDescription))
            throw new ArgumentException("返工问题描述不能为空。", nameof(request));
        var userId = _currentUser.GetUserId();
        var candidate = await _db.CandidateChapters.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == request.CandidateChapterId &&
            item.UserId == userId &&
            item.Version == request.CandidateVersion &&
            item.Status == "candidate",
            cancellationToken) ?? throw new KeyNotFoundException("候选章节版本不存在或不可返工。");
        var sourceSessionId = await _db.CreativeGoals.AsNoTracking()
            .Where(goal =>
                goal.Id == candidate.GoalId &&
                goal.UserId == userId &&
                goal.ProjectId == candidate.ProjectId)
            .Select(goal => goal.SourceSessionId)
            .SingleAsync(cancellationToken);
        if (!string.Equals(request.SessionId.Trim(), sourceSessionId, StringComparison.Ordinal))
            throw new InvalidOperationException("返工请求必须使用 Goal 的来源会话。");
        var artifact = await _db.KernelArtifacts.AsNoTracking().SingleAsync(item =>
            item.Id == candidate.CurrentArtifactId && item.UserId == userId,
            cancellationToken);
        var draftArtifact = JsonSerializer.Deserialize<ChapterDraftArtifact>(artifact.ContentJson)
            ?? throw new InvalidOperationException("候选正文 Artifact 无法解析。");
        ValidateSelection(request, draftArtifact.DraftContent);
        var context = new ReworkIntentCompilationContext(
            userId,
            candidate.ProjectId,
            candidate.GoalId,
            candidate.BranchId,
            candidate.Id,
            candidate.Version,
            draftArtifact.DraftContent,
            request.UserDescription.Trim(),
            request.SelectionStart,
            request.SelectionEnd,
            request.SelectedText);
        var compiled = await _model.CompileAsync(context, cancellationToken);
        ValidateCompiled(compiled, request);

        var now = DateTime.UtcNow;
        var intent = new ReworkIntent
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = candidate.ProjectId,
            GoalId = candidate.GoalId,
            BranchId = candidate.BranchId,
            CandidateChapterId = candidate.Id,
            CandidateVersion = candidate.Version,
            SessionId = sourceSessionId,
            TargetScope = compiled.TargetScope,
            SelectionStart = compiled.TargetScope == "selection" ? request.SelectionStart : null,
            SelectionEnd = compiled.TargetScope == "selection" ? request.SelectionEnd : null,
            SelectedText = compiled.TargetScope == "selection" ? request.SelectedText : string.Empty,
            UserDescription = context.UserDescription,
            Problem = compiled.Problem.Trim(),
            DesiredEffect = compiled.DesiredEffect.Trim(),
            PreserveJson = JsonSerializer.Serialize(compiled.Preserve),
            MayChangeJson = JsonSerializer.Serialize(compiled.MayChange),
            MustNotChangeJson = JsonSerializer.Serialize(compiled.MustNotChange),
            AcceptanceCriteriaJson = JsonSerializer.Serialize(compiled.AcceptanceCriteria),
            ImpactLevel = compiled.ImpactLevel,
            ImpactAssessmentJson = JsonSerializer.Serialize(new { summary = compiled.ImpactAssessment }),
            Status = "proposed",
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.ReworkIntents.Add(intent);
        await _db.SaveChangesAsync(cancellationToken);
        return intent;
    }

    public async Task<ReworkIntent> StartAutomaticAttemptAsync(
        string intentId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var intent = await _db.ReworkIntents.SingleOrDefaultAsync(item =>
            item.Id == intentId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("返工意图不存在或不属于当前用户。");
        if (!_budget.CanStartAutomaticAttempt(intent))
        {
            intent.Status = "needs_decision";
            intent.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("自动返工预算已耗尽或当前状态不允许启动返工。");
        }

        intent.AttemptCount++;
        intent.Status = "executing";
        intent.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return intent;
    }

    public async Task<ReworkIntent> RecordAttemptOutcomeAsync(
        string intentId,
        bool problemResolved,
        bool problemImproved,
        bool impactExpanded,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var intent = await _db.ReworkIntents.SingleOrDefaultAsync(item =>
            item.Id == intentId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("返工意图不存在或不属于当前用户。");
        if (intent.Status != "executing")
            throw new InvalidOperationException("只有执行中的返工意图可以记录结果。");

        intent.Status = problemResolved
            ? "resolved"
            : _budget.EvaluateOutcome(intent, problemImproved, impactExpanded);
        intent.ImpactAssessmentJson = JsonSerializer.Serialize(new
        {
            problemResolved,
            problemImproved,
            impactExpanded,
            propagation = _budget.GetPropagation(intent.ImpactLevel)
        });
        intent.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return intent;
    }

    private static void ValidateSelection(CompileReworkIntentRequest request, string content)
    {
        var hasStart = request.SelectionStart.HasValue;
        var hasEnd = request.SelectionEnd.HasValue;
        if (hasStart != hasEnd)
            throw new ArgumentException("选区起止位置必须同时提供。", nameof(request));
        if (!hasStart)
            return;
        if (request.SelectionStart < 0 || request.SelectionEnd <= request.SelectionStart || request.SelectionEnd > content.Length)
            throw new ArgumentOutOfRangeException(nameof(request), "选区超出候选正文范围。");
        if (string.IsNullOrWhiteSpace(request.SelectedText))
            throw new ArgumentException("提供选区位置时必须同时提供选中文本。", nameof(request));
    }

    private static void ValidateCompiled(ReworkIntentDraft compiled, CompileReworkIntentRequest request)
    {
        if (!TargetScopes.Contains(compiled.TargetScope))
            throw new InvalidOperationException("返工模型返回了未知 target_scope。");
        if (compiled.TargetScope == "selection" && !request.SelectionStart.HasValue)
            throw new InvalidOperationException("返工模型选择了 selection，但请求没有选区证据。");
        if (!ImpactLevels.Contains(compiled.ImpactLevel))
            throw new InvalidOperationException("返工模型返回了未知 impact_level。");
        if (string.IsNullOrWhiteSpace(compiled.Problem) ||
            string.IsNullOrWhiteSpace(compiled.DesiredEffect) ||
            compiled.AcceptanceCriteria.Count == 0)
            throw new InvalidOperationException("返工模型返回的合同字段不完整。");
    }
}

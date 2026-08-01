using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Kernels;
using TM.Web.NovelAgentWeb.Services.Rework;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/goals")]
[Authorize]
public sealed class GoalWorkflowController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICommitmentAssessmentService _commitments;
    private readonly ICreativeGoalService _goals;
    private readonly IGoalCompiler _compiler;
    private readonly IGoalControlService _control;
    private readonly ICanonBranchService _branches;
    private readonly IPrefixMergeService _prefixMerge;
    private readonly IReworkIntentService _rework;
    private readonly IGoalProgressEventPublisher _progress;
    private readonly IBookProductionService _bookProductions;

    public GoalWorkflowController(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        ICommitmentAssessmentService commitments,
        ICreativeGoalService goals,
        IGoalCompiler compiler,
        IGoalControlService control,
        ICanonBranchService branches,
        IPrefixMergeService prefixMerge,
        IReworkIntentService rework,
        IGoalProgressEventPublisher progress,
        IBookProductionService bookProductions)
    {
        _db = db;
        _currentUser = currentUser;
        _commitments = commitments;
        _goals = goals;
        _compiler = compiler;
        _control = control;
        _branches = branches;
        _prefixMerge = prefixMerge;
        _rework = rework;
        _progress = progress;
        _bookProductions = bookProductions;
    }

    [HttpPost("workflow/preview")]
    public async Task<IActionResult> Preview(
        [FromBody] CommitmentAssessmentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _commitments.AssessAsync(request, cancellationToken));

    [HttpPost("workflow/confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] CommitCreativeGoalRequest request,
        CancellationToken cancellationToken)
    {
        var assessment = await _commitments.AssessAsync(request.Assessment, cancellationToken);
        var response = await InSerializableTransactionAsync<IActionResult>(async () =>
        {
            var submission = await _goals.SubmitAsync(request.Command, assessment, cancellationToken);
            if (submission.Status is not (GoalSubmissionStatus.Created or GoalSubmissionStatus.Existing) ||
                string.IsNullOrWhiteSpace(submission.GoalId))
                return Conflict(submission);

            var graph = submission.Status == GoalSubmissionStatus.Existing
                ? await LoadExistingGraphOrCompileAsync(submission.GoalId, cancellationToken)
                : await _compiler.CompileAsync(submission.GoalId, cancellationToken);
            return Ok(new GoalWorkflowConfirmationResponse(submission, graph));
        }, cancellationToken);
        if (response is OkObjectResult { Value: GoalWorkflowConfirmationResponse confirmation } &&
            !string.IsNullOrWhiteSpace(confirmation.Submission.GoalId))
        {
            await _progress.PublishAsync(new GoalProgressEventRequest(
                _currentUser.GetUserId(),
                confirmation.Submission.GoalId,
                AgentSseEventType.GoalCommitted,
                "Creative Goal 已确认并编译为持久任务图。",
                Action: confirmation.Submission.Status.ToString()), cancellationToken);
        }
        return response;
    }

    private async Task<TaskGraphDefinition> LoadExistingGraphOrCompileAsync(
        string goalId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        var persisted = await _db.TaskGraphVersions.AsNoTracking()
            .Where(item => item.UserId == userId && item.GoalId == goalId)
            .OrderByDescending(item => item.Version)
            .Select(item => item.GraphJson)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(persisted))
            return await _compiler.CompileAsync(goalId, cancellationToken);

        return JsonSerializer.Deserialize<TaskGraphDefinition>(persisted, JsonOptions)
            ?? throw new InvalidOperationException("已持久化的 Goal 任务图为空。");
    }

    [HttpPost("{goalId}/workflow/revisions")]
    public async Task<IActionResult> Revise(
        string goalId,
        [FromBody] ReviseCreativeGoalApiRequest request,
        CancellationToken cancellationToken)
    {
        var response = await InSerializableTransactionAsync<IActionResult>(async () =>
        {
            await RequireGoalAsync(goalId, cancellationToken);
            var revision = await _goals.ReviseAsync(new ReviseCreativeGoalCommand(
                goalId,
                request.Reason,
                request.ConstraintChangesJson,
                request.ReusableArtifactIds,
                request.InvalidatedArtifactIds,
                request.AffectedNodeIds), cancellationToken);
            var graph = await _compiler.RecompileForRevisionAsync(
                goalId,
                revision.Id,
                request.AffectedNodeIds,
                cancellationToken);
            return Ok(new { revision, graph });
        }, cancellationToken);
        await _progress.PublishAsync(new GoalProgressEventRequest(
            _currentUser.GetUserId(),
            goalId,
            AgentSseEventType.GoalRevised,
            "Goal 约束已修订并生成新的任务图版本。",
            Action: "revised"), cancellationToken);
        return response;
    }

    [HttpGet("{goalId}/workflow")]
    public async Task<IActionResult> GetStatus(string goalId, CancellationToken cancellationToken)
    {
        var goal = await RequireGoalAsync(goalId, cancellationToken);
        return Ok(await BuildStatusAsync(goal, cancellationToken));
    }

    [HttpGet("project/{projectId}/latest/workflow")]
    public async Task<IActionResult> GetLatestProjectStatus(string projectId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        var goal = await _db.CreativeGoals.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return Ok(goal == null ? null : await BuildStatusAsync(goal, cancellationToken));
    }

    private async Task<GoalWorkflowStatusResponse> BuildStatusAsync(
        CreativeGoal goal,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        var graph = await _db.TaskGraphVersions.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == goal.ProjectId && item.GoalId == goal.Id)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        var tasks = graph == null
            ? []
            : await _db.KernelTasks.AsNoTracking()
                .Where(item =>
                    item.UserId == userId &&
                    item.ProjectId == goal.ProjectId &&
                    item.GoalId == goal.Id &&
                    item.TaskGraphVersionId == graph.Id)
                .OrderBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);
        var branches = await _db.CanonBranches.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == goal.ProjectId && item.GoalId == goal.Id)
            .OrderBy(item => item.StartChapterNumber)
            .ToListAsync(cancellationToken);
        var candidates = await _db.CandidateChapters.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.ProjectId == goal.ProjectId &&
                item.GoalId == goal.Id)
            .OrderBy(item => item.ChapterNumber)
            .ThenByDescending(item => item.Version)
            .Select(item => new GoalChapterSummaryResponse(
                item.Id,
                item.ChapterId,
                item.ChapterNumber,
                item.Version,
                item.Status,
                item.Authorship,
                item.IsProtected,
                item.CurrentArtifactId,
                item.BranchId))
            .ToListAsync(cancellationToken);
        var candidateCount = candidates.Count;
        var production = await _db.BookProductions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == userId && item.GoalId == goal.Id,
            cancellationToken);
        var batches = production == null
            ? []
            : await _db.ProductionBatches.AsNoTracking()
                .Where(item => item.UserId == userId && item.BookProductionId == production.Id)
                .OrderBy(item => item.BatchNumber)
                .ToListAsync(cancellationToken);
        return new GoalWorkflowStatusResponse(goal, production, batches, graph, tasks, branches, candidates, candidateCount);
    }

    [HttpGet("{goalId}/workflow/chapters/{chapterNumber:int}")]
    public async Task<IActionResult> GetChapter(
        string goalId,
        int chapterNumber,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        var goal = await RequireGoalAsync(goalId, cancellationToken);
        var candidate = await _db.CandidateChapters.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.ProjectId == goal.ProjectId &&
                item.GoalId == goal.Id &&
                item.ChapterNumber == chapterNumber)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("候选章节不存在或不属于当前用户。");
        var draft = await _db.KernelArtifacts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == candidate.CurrentArtifactId &&
            item.UserId == userId &&
            item.ProjectId == goal.ProjectId &&
            item.GoalId == goal.Id &&
            item.BranchId == candidate.BranchId,
            cancellationToken) ?? throw new KeyNotFoundException("候选正文证据不存在。");
        var reviewIds = JsonSerializer.Deserialize<string[]>(candidate.ReviewArtifactIdsJson) ?? [];
        var reviews = reviewIds.Length == 0
            ? []
            : await _db.KernelArtifacts.AsNoTracking()
                .Where(item =>
                    item.UserId == userId &&
                    item.ProjectId == goal.ProjectId &&
                    item.GoalId == goal.Id &&
                    item.BranchId == candidate.BranchId &&
                    reviewIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var citations = await _db.KnowledgeCitations.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.ProjectId == goal.ProjectId &&
                item.GoalId == goal.Id &&
                item.ChapterId == candidate.ChapterId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        return Ok(new GoalChapterDetailResponse(candidate, draft, reviews, citations));
    }

    [HttpPost("{goalId}/workflow/chapters/{chapterNumber:int}/rework")]
    public async Task<IActionResult> Rework(
        string goalId,
        int chapterNumber,
        [FromBody] GoalChapterReworkRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("返工请求必须提供幂等键。", nameof(request));
        var response = await InSerializableTransactionAsync<IActionResult>(async () =>
        {
        var userId = _currentUser.GetUserId();
        var goal = await RequireGoalAsync(goalId, cancellationToken);
        var taskIdempotencyKey = ReworkTaskIdempotencyKey(goal.Id, request.IdempotencyKey);
        await AcquireReworkIdempotencyLockAsync(userId, taskIdempotencyKey, cancellationToken);
        var existing = await _db.KernelTasks.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == userId && item.IdempotencyKey == taskIdempotencyKey,
            cancellationToken);
        if (existing != null)
            return Ok(new GoalChapterReworkResponse(existing.Id, IntentArtifactId(existing.InputArtifactIdsJson), existing.Status));

        var candidate = await _db.CandidateChapters.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == request.CandidateChapterId &&
            item.UserId == userId &&
            item.ProjectId == goal.ProjectId &&
            item.GoalId == goal.Id &&
            item.ChapterNumber == chapterNumber &&
            item.Version == request.CandidateVersion,
            cancellationToken) ?? throw new KeyNotFoundException("候选章节不存在、版本不匹配或不属于当前用户。");
        var originalExists = await _db.KernelArtifacts.AsNoTracking().AnyAsync(item =>
            item.Id == candidate.CurrentArtifactId &&
            item.UserId == userId &&
            item.ProjectId == goal.ProjectId &&
            item.GoalId == goal.Id &&
            item.BranchId == candidate.BranchId,
            cancellationToken);
        if (!originalExists)
            throw new KeyNotFoundException("候选正文 Artifact 不存在。");
        var graph = await _db.TaskGraphVersions.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == goal.ProjectId && item.GoalId == goal.Id)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Goal 尚未编译任务图。");
        var contextArtifactId = await _db.KernelArtifacts.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.ProjectId == goal.ProjectId &&
                item.GoalId == goal.Id &&
                item.BranchId == candidate.BranchId &&
                item.TaskId == $"{graph.Id}:chapter-{chapterNumber}-context" &&
                item.ArtifactType == "ChapterContextContract")
            .Select(item => item.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("返工缺少该章冻结的 ChapterContextContract。");
        var intent = await _rework.CompileAsync(new CompileReworkIntentRequest(
            candidate.Id,
            candidate.Version,
            request.SessionId,
            request.UserDescription,
            request.SelectionStart,
            request.SelectionEnd,
            request.SelectedText), cancellationToken);
        await _rework.StartAutomaticAttemptAsync(intent.Id, cancellationToken);
        var intentJson = JsonSerializer.Serialize(new ReworkIntentArtifactContract(
            intent.Id,
            intent.TargetScope,
            intent.SelectionStart,
            intent.SelectionEnd,
            intent.Problem,
            intent.DesiredEffect,
            DeserializeList(intent.PreserveJson),
            DeserializeList(intent.MayChangeJson),
            DeserializeList(intent.MustNotChangeJson),
            DeserializeList(intent.AcceptanceCriteriaJson)), JsonOptions);
        var nodePrefix = $"manual-rework-{Sha256(taskIdempotencyKey)[..16]}";
        var draftNode = $"{nodePrefix}-draft";
        var continuityNode = $"{nodePrefix}-continuity-review";
        var literaryNode = $"{nodePrefix}-literary-review";
        var adoptNode = $"{nodePrefix}-adopt";
        var summaryNode = $"{nodePrefix}-continuity-summary";
        var now = DateTime.UtcNow;
        var draftTask = ReworkTask(
            graph.Id,
            draftNode,
            "tianming_writing",
            "DirectedReworkDraft",
            "ready",
            [],
            [contextArtifactId, candidate.CurrentArtifactId],
            taskIdempotencyKey,
            100,
            userId,
            goal,
            candidate.BranchId,
            now);
        var intentArtifact = new KernelArtifact
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = goal.ProjectId,
            GoalId = goal.Id,
            TaskId = draftTask.Id,
            BranchId = candidate.BranchId,
            ArtifactType = "ReworkIntent",
            ContentJson = intentJson,
            ContentHash = Sha256(intentJson),
            Status = "adopted",
            Authorship = "human"
        };
        draftTask.InputArtifactIdsJson = JsonSerializer.Serialize(
            new[] { contextArtifactId, candidate.CurrentArtifactId, intentArtifact.Id }, JsonOptions);
        var continuityTask = ReworkTask(
            graph.Id, continuityNode, "continuity_review", "ReviewContinuity", "blocked",
            [draftNode], [], $"{taskIdempotencyKey}:continuity", 101, userId, goal, candidate.BranchId, now);
        var literaryTask = ReworkTask(
            graph.Id, literaryNode, "literary_review", "ReviewLiteraryQuality", "blocked",
            [draftNode], [], $"{taskIdempotencyKey}:literary", 102, userId, goal, candidate.BranchId, now);
        var adoptTask = ReworkTask(
            graph.Id, adoptNode, "tianming_writing", "DirectedRework", "blocked",
            [draftNode, continuityNode, literaryNode], [contextArtifactId],
            $"{taskIdempotencyKey}:adopt", 103, userId, goal, candidate.BranchId, now);
        var summaryTask = ReworkTask(
            graph.Id, summaryNode, "continuity_review", "ExtractContinuitySummary", "blocked",
            [adoptNode], [], $"{taskIdempotencyKey}:summary", 104, userId, goal, candidate.BranchId, now);
        _db.KernelArtifacts.Add(intentArtifact);
        _db.KernelTasks.AddRange(draftTask, continuityTask, literaryTask, adoptTask, summaryTask);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new GoalChapterReworkResponse(draftTask.Id, intentArtifact.Id, draftTask.Status));
        }, cancellationToken);
        if (response is OkObjectResult { Value: GoalChapterReworkResponse rework })
        {
            await _progress.PublishAsync(new GoalProgressEventRequest(
                _currentUser.GetUserId(),
                goalId,
                AgentSseEventType.GoalStateChanged,
                $"第 {chapterNumber} 章返工任务已进入队列。",
                TaskId: rework.TaskId,
                ChapterNumber: chapterNumber,
                Action: "rework_queued"), cancellationToken);
        }
        return response;
    }

    [HttpPost("{goalId}/workflow/chapters/{chapterNumber:int}/manual-edit")]
    public async Task<IActionResult> SaveManualEdit(
        string goalId,
        int chapterNumber,
        [FromBody] GoalChapterManualEditRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("人工正文不能为空。", nameof(request));

        var response = await InSerializableTransactionAsync<IActionResult>(async () =>
        {
            var userId = _currentUser.GetUserId();
            var candidate = await RequireCandidateAsync(
                goalId,
                chapterNumber,
                request.CandidateChapterId,
                request.CandidateVersion,
                cancellationToken);
            var currentArtifact = await _db.KernelArtifacts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == candidate.CurrentArtifactId &&
                item.UserId == userId &&
                item.ProjectId == candidate.ProjectId &&
                item.GoalId == candidate.GoalId &&
                item.BranchId == candidate.BranchId,
                cancellationToken) ?? throw new KeyNotFoundException("候选正文 Artifact 不存在。");
            var currentDraft = JsonSerializer.Deserialize<ChapterDraftArtifact>(currentArtifact.ContentJson, JsonOptions)
                ?? throw new InvalidOperationException("候选正文 Artifact 无法解析。");
            if (string.Equals(currentDraft.DraftContent, request.Content, StringComparison.Ordinal))
                throw new InvalidOperationException("人工正文没有发生变化。");

            var artifactId = Guid.NewGuid().ToString("N");
            var editedDraft = new ChapterDraftArtifact
            {
                ArtifactId = artifactId,
                ChapterId = candidate.ChapterId,
                Status = "human_edited",
                DraftContent = request.Content,
                CommittedContent = currentDraft.DraftContent,
                ChangesJson = JsonSerializer.Serialize(new
                {
                    sourceCandidateChapterId = candidate.Id,
                    sourceCandidateVersion = candidate.Version
                }, JsonOptions),
                HasChanges = true,
                GeneratedAt = DateTime.UtcNow
            };
            var contentJson = JsonSerializer.Serialize(editedDraft, JsonOptions);
            _db.KernelArtifacts.Add(new KernelArtifact
            {
                Id = artifactId,
                UserId = userId,
                ProjectId = candidate.ProjectId,
                GoalId = candidate.GoalId,
                TaskId = currentArtifact.TaskId,
                BranchId = candidate.BranchId,
                ArtifactType = "CandidateChapterDraft",
                SchemaVersion = 1,
                ContentJson = contentJson,
                ContentHash = Sha256(contentJson),
                Status = "adopted",
                Authorship = "human",
                IsProtected = true,
                CausationId = currentArtifact.Id,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            var editedCandidate = await _branches.AddCandidateAsync(
                candidate.BranchId,
                candidate.ChapterId,
                candidate.ChapterNumber,
                artifactId,
                candidate.DependsOnCandidateChapterId,
                "human",
                true,
                cancellationToken);
            return Ok(new GoalChapterManualEditResponse(
                editedCandidate.Id,
                editedCandidate.Version,
                artifactId,
                editedCandidate.Authorship,
                editedCandidate.IsProtected));
        }, cancellationToken);
        if (response is OkObjectResult { Value: GoalChapterManualEditResponse edited })
        {
            await _progress.PublishAsync(new GoalProgressEventRequest(
                _currentUser.GetUserId(),
                goalId,
                AgentSseEventType.GoalCandidateChanged,
                $"第 {chapterNumber} 章人工候选版本 v{edited.CandidateVersion} 已保存。",
                CandidateChapterId: edited.CandidateChapterId,
                CandidateVersion: edited.CandidateVersion,
                ChapterNumber: chapterNumber,
                ArtifactIds: [edited.ArtifactId],
                Action: "manual_edit"), cancellationToken);
        }
        return response;
    }

    [HttpPost("{goalId}/workflow/chapters/{chapterNumber:int}/accept")]
    public async Task<IActionResult> Accept(
        string goalId,
        int chapterNumber,
        [FromBody] GoalChapterAcceptRequest request,
        CancellationToken cancellationToken)
    {
        var candidate = await RequireCandidateAsync(goalId, chapterNumber, request.CandidateChapterId, request.CandidateVersion, cancellationToken);
        var acceptance = await _branches.AcceptAsync(candidate.Id, candidate.Version, cancellationToken);
        await _progress.PublishAsync(new GoalProgressEventRequest(
            _currentUser.GetUserId(),
            goalId,
            AgentSseEventType.GoalCandidateAccepted,
            $"第 {chapterNumber} 章候选版本 v{candidate.Version} 已验收。",
            CandidateChapterId: candidate.Id,
            CandidateVersion: candidate.Version,
            ChapterNumber: chapterNumber,
            BranchId: candidate.BranchId,
            ArtifactIds: [$"acceptance-decision:{acceptance.Id}"],
            Action: "accepted"), cancellationToken);
        return Ok(acceptance);
    }

    [HttpPost("{goalId}/workflow/merge-prefix")]
    public async Task<IActionResult> MergePrefix(
        string goalId,
        [FromBody] GoalPrefixMergeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        var goal = await RequireGoalAsync(goalId, cancellationToken);
        var branch = await _db.CanonBranches.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == request.BranchId && item.UserId == userId && item.ProjectId == goal.ProjectId && item.GoalId == goal.Id,
            cancellationToken);
        if (branch == null)
            throw new KeyNotFoundException("候选分支不存在或不属于当前用户。");
        var merge = await _prefixMerge.MergeAcceptedPrefixAsync(request.BranchId, cancellationToken);
        BookProductionAdvanceResult? advance = null;
        if (merge.EndChapterNumber == branch.EndChapterNumber)
        {
            advance = await _bookProductions.FinalizeMergedBatchAsync(goal.Id, branch.Id, cancellationToken);
            if (advance.ShouldCompileNextBatch)
                await _compiler.CompileAsync(goal.Id, cancellationToken);
        }
        await _progress.PublishAsync(new GoalProgressEventRequest(
            userId,
            goalId,
            AgentSseEventType.GoalPrefixMerged,
            $"第 {merge.StartChapterNumber}-{merge.EndChapterNumber} 章已合并到正史。",
            BranchId: branch.Id,
            ArtifactIds: [$"merge-record:{merge.Id}"],
            Action: "prefix_merged"), cancellationToken);
        return Ok(merge);
    }

    [HttpPost("{goalId}/workflow/strategy")]
    public async Task<IActionResult> ChangeStrategy(
        string goalId,
        [FromBody] ChangeBookExecutionStrategyRequest request,
        CancellationToken cancellationToken)
    {
        await RequireGoalAsync(goalId, cancellationToken);
        var production = await _bookProductions.ChangeStrategyAsync(goalId, request.ExecutionStrategy, cancellationToken);
        return Ok(production);
    }

    [HttpPost("{goalId}/workflow/continue-batch")]
    public async Task<IActionResult> ContinueBatch(string goalId, CancellationToken cancellationToken)
    {
        await RequireGoalAsync(goalId, cancellationToken);
        await _bookProductions.ContinueInteractiveAsync(goalId, cancellationToken);
        var graph = await _compiler.CompileAsync(goalId, cancellationToken);
        return Ok(graph);
    }

    [HttpPost("{goalId}/workflow/pause")]
    public async Task<IActionResult> Pause(string goalId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        await RequireGoalAsync(goalId, cancellationToken);
        var status = await _control.RequestPauseAsync(userId, goalId, cancellationToken);
        await _progress.PublishAsync(new GoalProgressEventRequest(
            userId,
            goalId,
            AgentSseEventType.GoalStateChanged,
            status == "paused" ? "Goal 已暂停。" : "Goal 将在下一个安全点暂停。",
            Action: status), cancellationToken);
        return Ok(new GoalWorkflowControlResponse(goalId, status));
    }

    [HttpPost("{goalId}/workflow/resume")]
    public async Task<IActionResult> Resume(string goalId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        await RequireGoalAsync(goalId, cancellationToken);
        await _control.ResumeAsync(userId, goalId, cancellationToken);
        await _progress.PublishAsync(new GoalProgressEventRequest(
            userId,
            goalId,
            AgentSseEventType.GoalStateChanged,
            "Goal 已恢复执行。",
            Action: "resumed"), cancellationToken);
        return Ok(new GoalWorkflowControlResponse(goalId, "resumed"));
    }

    [HttpPost("{goalId}/workflow/cancel")]
    public async Task<IActionResult> Cancel(
        string goalId,
        [FromBody] CancelGoalRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        await RequireGoalAsync(goalId, cancellationToken);
        await _control.CancelAsync(userId, goalId, request.Strategy, cancellationToken);
        await _progress.PublishAsync(new GoalProgressEventRequest(
            userId,
            goalId,
            AgentSseEventType.GoalStateChanged,
            "Goal 已取消。",
            Action: request.Strategy.ToString()), cancellationToken);
        return Ok(new GoalWorkflowControlResponse(goalId, "canceled", request.Strategy));
    }

    private async Task<CreativeGoal> RequireGoalAsync(string goalId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        return await _db.CreativeGoals.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == goalId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
    }

    private async Task<CandidateChapter> RequireCandidateAsync(
        string goalId,
        int chapterNumber,
        string candidateChapterId,
        int candidateVersion,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        var goal = await RequireGoalAsync(goalId, cancellationToken);
        return await _db.CandidateChapters.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == candidateChapterId &&
            item.Version == candidateVersion &&
            item.ChapterNumber == chapterNumber &&
            item.UserId == userId &&
            item.ProjectId == goal.ProjectId &&
            item.GoalId == goal.Id,
            cancellationToken) ?? throw new KeyNotFoundException("候选章节不存在或不属于当前用户。");
    }

    private static IReadOnlyList<string> DeserializeList(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static KernelTask ReworkTask(
        string graphId,
        string nodeId,
        string kernelName,
        string taskType,
        string status,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<string> inputArtifactIds,
        string idempotencyKey,
        int priority,
        string userId,
        CreativeGoal goal,
        string branchId,
        DateTime now) => new()
    {
        Id = $"{graphId}:{nodeId}",
        UserId = userId,
        ProjectId = goal.ProjectId,
        GoalId = goal.Id,
        TaskGraphVersionId = graphId,
        BranchId = branchId,
        KernelName = kernelName,
        TaskType = taskType,
        Status = status,
        DependencyTaskIdsJson = JsonSerializer.Serialize(dependencies, JsonOptions),
        InputArtifactIdsJson = JsonSerializer.Serialize(inputArtifactIds, JsonOptions),
        IdempotencyKey = idempotencyKey,
        MaxAttempts = 1,
        Priority = priority,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static string ReworkTaskIdempotencyKey(string goalId, string requestKey)
    {
        var raw = $"goal-rework:{goalId}:{requestKey.Trim()}";
        return raw.Length <= 160 ? raw : $"goal-rework:{Sha256(raw)}";
    }

    private static string? IntentArtifactId(string inputArtifactIdsJson)
    {
        var ids = JsonSerializer.Deserialize<string[]>(inputArtifactIdsJson) ?? [];
        return ids.LastOrDefault();
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private async Task AcquireReworkIdempotencyLockAsync(
        string userId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational() ||
            _db.Database.GetDbConnection() is not Npgsql.NpgsqlConnection)
            return;
        var lockKey = $"goal-rework\u001f{userId}\u001f{idempotencyKey}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }

    private async Task<T> InSerializableTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction != null)
            return await action();

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

public sealed record GoalWorkflowConfirmationResponse(
    GoalSubmissionResult Submission,
    TaskGraphDefinition Graph);

public sealed record GoalWorkflowStatusResponse(
    CreativeGoal Goal,
    BookProduction? Production,
    IReadOnlyList<ProductionBatch> Batches,
    TaskGraphVersion? Graph,
    IReadOnlyList<KernelTask> Tasks,
    IReadOnlyList<CanonBranch> Branches,
    IReadOnlyList<GoalChapterSummaryResponse> Candidates,
    int CandidateChapterCount);

public sealed record GoalChapterSummaryResponse(
    string Id,
    string ChapterId,
    int ChapterNumber,
    int Version,
    string Status,
    string Authorship,
    bool IsProtected,
    string CurrentArtifactId,
    string BranchId);

public sealed record GoalChapterDetailResponse(
    CandidateChapter Candidate,
    KernelArtifact DraftArtifact,
    IReadOnlyList<KernelArtifact> ReviewArtifacts,
    IReadOnlyList<KnowledgeCitation> Citations);

public sealed record GoalChapterReworkRequest(
    string CandidateChapterId,
    int CandidateVersion,
    string SessionId,
    string UserDescription,
    int? SelectionStart,
    int? SelectionEnd,
    string SelectedText,
    string IdempotencyKey);

public sealed record GoalChapterReworkResponse(string TaskId, string? IntentArtifactId, string Status);
public sealed record GoalChapterManualEditRequest(
    string CandidateChapterId,
    int CandidateVersion,
    string Content);
public sealed record GoalChapterManualEditResponse(
    string CandidateChapterId,
    int CandidateVersion,
    string ArtifactId,
    string Authorship,
    bool IsProtected);
public sealed record GoalChapterAcceptRequest(string CandidateChapterId, int CandidateVersion);
public sealed record GoalPrefixMergeRequest(string BranchId);
public sealed record ChangeBookExecutionStrategyRequest(string ExecutionStrategy);

public sealed record CommitCreativeGoalRequest(
    CommitmentAssessmentRequest Assessment,
    CreateCreativeGoalCommand Command);

public sealed record ReviseCreativeGoalApiRequest(
    string Reason,
    string ConstraintChangesJson,
    IReadOnlyList<string> ReusableArtifactIds,
    IReadOnlyList<string> InvalidatedArtifactIds,
    IReadOnlyList<string> AffectedNodeIds);

public sealed record CancelGoalRequest(GoalCancellationStrategy Strategy);
public sealed record GoalWorkflowControlResponse(
    string GoalId,
    string Status,
    GoalCancellationStrategy? CancellationStrategy = null);

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using Tianming.NovelAgent.Application.Ports;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class GoalCompiler : IGoalCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly TaskGraphValidator _validator;
    private readonly IBookProductionService _bookProductions;
    private readonly ILegacyControlPlaneCommands _controlPlane;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public GoalCompiler(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        TaskGraphValidator validator,
        IBookProductionService bookProductions,
        ILegacyControlPlaneCommands controlPlane)
    {
        _db = db;
        _currentUser = currentUser;
        _validator = validator;
        _bookProductions = bookProductions;
        _controlPlane = controlPlane;
    }

    public GoalCompiler(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        TaskGraphValidator validator,
        IBookProductionService bookProductions)
        : this(db, currentUser, validator, bookProductions, LegacyControlPlaneCommands.Unconfigured)
    {
    }

    public async Task<TaskGraphDefinition> CompileAsync(
        string goalId,
        CancellationToken cancellationToken = default) =>
        await CompileInternalAsync(goalId, null, [], cancellationToken);

    public async Task<TaskGraphDefinition> RecompileForRevisionAsync(
        string goalId,
        string revisionId,
        IReadOnlyCollection<string> affectedNodeIds,
        CancellationToken cancellationToken = default)
    {
        if (affectedNodeIds.Count == 0)
            throw new ArgumentException("Goal Revision 必须声明受影响任务。", nameof(affectedNodeIds));

        return await CompileInternalAsync(goalId, revisionId, affectedNodeIds, cancellationToken);
    }

    private async Task<TaskGraphDefinition> CompileInternalAsync(
        string goalId,
        string? revisionId,
        IReadOnlyCollection<string> affectedNodeIds,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserId();
        var committedGoal = await _db.CreativeGoals
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == goalId && item.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
        GoalRevision? targetRevision = null;
        if (revisionId != null)
        {
            targetRevision = await _db.GoalRevisions.AsNoTracking().SingleOrDefaultAsync(
                revision =>
                    revision.Id == revisionId &&
                    revision.GoalId == committedGoal.Id &&
                    revision.UserId == userId,
                cancellationToken);
            if (targetRevision == null)
                throw new KeyNotFoundException("Goal Revision 不存在或不属于当前用户。");
        }
        var goal = await BuildEffectiveGoalAsync(
            committedGoal,
            targetRevision?.RevisionNumber,
            cancellationToken);

        var previousGraph = await _db.TaskGraphVersions
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.GoalId == goal.Id)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        var currentBatch = await _bookProductions.GetCurrentBatchAsync(goal.Id, cancellationToken);
        var range = new ChapterRange(currentBatch.StartChapterNumber, currentBatch.EndChapterNumber);
        var branchIdForBatch = await EnsureBatchStateAsync(goal, range, cancellationToken);
        var nextVersion = (await _db.TaskGraphVersions
            .Where(item => item.UserId == userId && item.GoalId == goal.Id)
            .Select(item => (int?)item.Version)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var nodes = BuildSkeleton(range.Start, range.End);
        var definition = new TaskGraphDefinition(goal.Id, nextVersion, nodes);
        _validator.ValidateOrThrow(definition);
        var invalidatedNodeIds = revisionId == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : FindAffectedDescendants(nodes, affectedNodeIds);
        var previousTasks = previousGraph == null
            ? new Dictionary<string, KernelTask>(StringComparer.Ordinal)
            : (await _db.KernelTasks.AsNoTracking()
                .Where(task => task.TaskGraphVersionId == previousGraph.Id)
                .ToListAsync(cancellationToken))
                .ToDictionary(TaskNodeId, StringComparer.Ordinal);

        var graphId = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;
        var graphJson = JsonSerializer.Serialize(definition, JsonOptions);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(graphJson))).ToLowerInvariant();
        var compiledTasks = nodes.Select((node, index) =>
        {
            previousTasks.TryGetValue(node.Id, out var previousTask);
            var canReuse = revisionId != null &&
                !invalidatedNodeIds.Contains(node.Id) &&
                previousTask != null &&
                previousTask.Status == "completed";
            var status = "blocked";
            if (canReuse)
                status = "reused";
            else if (node.DependsOn.Count == 0)
                status = "ready";

            var branchId = node.ChapterNumber.HasValue ||
                node.TaskType is BookProductionWorkflow.AcceptanceGate or BookProductionWorkflow.PrefixMerge
                ? branchIdForBatch
                : null;
            return new LegacyCompiledTask(
                node.Id,
                node.TaskType,
                node.KernelName,
                status,
                JsonSerializer.Serialize(node.DependsOn, JsonOptions),
                "[]",
                canReuse ? previousTask!.OutputArtifactIdsJson : "[]",
                KernelTaskFailurePolicy.MaxAttempts(node.TaskType),
                index,
                node.ChapterNumber,
                branchId,
                canReuse);
        }).ToArray();

        await _controlPlane.PersistCompiledGraphAsync(new LegacyCompiledGraphCommand(
            userId,
            goal.ProjectId,
            goal.Id,
            revisionId,
            graphId,
            nextVersion,
            graphJson,
            contentHash,
            branchIdForBatch,
            range.Start,
            range.End,
            compiledTasks,
            createdAt), cancellationToken);
        return definition;
    }

    private async Task<CreativeGoal> BuildEffectiveGoalAsync(
        CreativeGoal committedGoal,
        int? targetRevisionNumber,
        CancellationToken cancellationToken)
    {
        var revisions = await _db.GoalRevisions.AsNoTracking()
            .Where(item =>
                item.UserId == committedGoal.UserId &&
                item.GoalId == committedGoal.Id &&
                (!targetRevisionNumber.HasValue || item.RevisionNumber <= targetRevisionNumber.Value))
            .OrderBy(item => item.RevisionNumber)
            .ToListAsync(cancellationToken);
        return CreativeGoalRevisionProjector.Project(committedGoal, revisions);
    }

    private async Task<string> EnsureBatchStateAsync(
        CreativeGoal goal,
        ChapterRange range,
        CancellationToken cancellationToken)
    {
        var branch = await _db.CanonBranches
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.UserId == goal.UserId &&
                item.ProjectId == goal.ProjectId &&
                item.GoalId == goal.Id &&
                item.Status == "active",
                cancellationToken);
        if (branch is not null &&
            (branch.StartChapterNumber != range.Start || branch.EndChapterNumber > range.End))
        {
            throw new InvalidOperationException("活动候选分支与 Goal 章节范围不一致。");
        }
        var branchId = branch?.Id ?? Guid.NewGuid().ToString("N");

        var existingNumbers = await _db.Chapters.AsNoTracking()
            .Where(chapter =>
                chapter.ProjectId == goal.ProjectId &&
                chapter.ChapterNumber >= range.Start &&
                chapter.ChapterNumber <= range.End)
            .Select(chapter => chapter.ChapterNumber)
            .ToListAsync(cancellationToken);
        var existing = existingNumbers.ToHashSet();
        for (var chapterNumber = range.Start; chapterNumber <= range.End; chapterNumber++)
        {
            if (existing.Contains(chapterNumber))
                continue;
            _db.Chapters.Add(new Chapter
            {
                Id = Guid.NewGuid().ToString("N"),
                ProjectId = goal.ProjectId,
                ChapterNumber = chapterNumber,
                Title = $"第{chapterNumber}章",
                IdempotencyKey = $"goal:{goal.Id}:chapter:{chapterNumber}",
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return branchId;
    }

    private static HashSet<string> FindAffectedDescendants(
        IReadOnlyList<TaskGraphNode> nodes,
        IReadOnlyCollection<string> affectedNodeIds)
    {
        var known = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = affectedNodeIds.Where(id => !known.Contains(id)).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"Revision 引用了不存在的任务：{string.Join(", ", unknown)}");

        var affected = affectedNodeIds.ToHashSet(StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var node in nodes)
            {
                if (affected.Contains(node.Id) || !node.DependsOn.Any(affected.Contains))
                    continue;
                affected.Add(node.Id);
                changed = true;
            }
        }

        return affected;
    }

    private static string TaskNodeId(KernelTask task)
    {
        var separator = task.Id.IndexOf(':');
        return separator < 0 ? task.Id : task.Id[(separator + 1)..];
    }

    private static List<TaskGraphNode> BuildSkeleton(int startChapter, int endChapter)
    {
        var nodes = new List<TaskGraphNode>
        {
            Node("freeze-baselines", "FreezeBaselines", "context_compiler", TaskExecutionKind.System,
                [], [], ["FrozenBaselines"]),
            Node("analyze-creative-requirements", "AnalyzeCreativeRequirements", "narrative_planning", TaskExecutionKind.Kernel,
                ["freeze-baselines"], ["FrozenBaselines"], ["CreativeRequirements"]),
            Node("compile-batch-plan", "CompileBatchPlan", "narrative_planning", TaskExecutionKind.Kernel,
                ["freeze-baselines", "analyze-creative-requirements"],
                ["FrozenBaselines", "CreativeRequirements"], ["BatchPlan"])
        };

        for (var chapter = startChapter; chapter <= endChapter; chapter++)
        {
            var previousSummary = chapter == startChapter
                ? Array.Empty<string>()
                : new[] { $"chapter-{chapter - 1}-continuity-summary" };
            var planDependencies = new[] { "compile-batch-plan" }.Concat(previousSummary).ToArray();
            nodes.Add(Node($"chapter-{chapter}-plan", "PlanChapter", "narrative_planning", TaskExecutionKind.Kernel,
                planDependencies, ["BatchPlan"], ["ChapterPlan"], chapter: chapter));
            nodes.Add(Node($"chapter-{chapter}-context", "CompileChapterContext", "knowledge_retrieval", TaskExecutionKind.Kernel,
                [$"chapter-{chapter}-plan", "freeze-baselines"], ["ChapterPlan", "FrozenBaselines"], ["ChapterContextContract", "EvidenceBundle"], chapter: chapter));
            nodes.Add(Node($"chapter-{chapter}-write", "WriteCandidate", "tianming_writing", TaskExecutionKind.Kernel,
                [$"chapter-{chapter}-context"], ["ChapterContextContract", "EvidenceBundle"], ["CandidateChapterDraft"], chapter: chapter));
            nodes.Add(Node($"chapter-{chapter}-continuity-review", "ReviewContinuity", "continuity_review", TaskExecutionKind.Kernel,
                [$"chapter-{chapter}-write"], ["CandidateChapterDraft", "EvidenceBundle"], ["ContinuityReview"], chapter: chapter));
            nodes.Add(Node($"chapter-{chapter}-literary-review", "ReviewLiteraryQuality", "literary_review", TaskExecutionKind.Kernel,
                [$"chapter-{chapter}-write"], ["CandidateChapterDraft"], ["LiteraryReview"], chapter: chapter));
            nodes.Add(Node($"chapter-{chapter}-directed-rework", "DirectedRework", "tianming_writing", TaskExecutionKind.Kernel,
                [$"chapter-{chapter}-context", $"chapter-{chapter}-write", $"chapter-{chapter}-continuity-review", $"chapter-{chapter}-literary-review"],
                ["ChapterContextContract", "CandidateChapterDraft", "ContinuityReview", "LiteraryReview"], ["ReviewedCandidateChapter"], chapter: chapter));
            nodes.Add(Node($"chapter-{chapter}-continuity-summary", "ExtractContinuitySummary", "continuity_review", TaskExecutionKind.Kernel,
                [$"chapter-{chapter}-directed-rework"], ["ReviewedCandidateChapter"], ["ContinuitySummary"], chapter: chapter));
        }

        var summaries = Enumerable.Range(startChapter, endChapter - startChapter + 1)
            .Select(chapter => $"chapter-{chapter}-continuity-summary")
            .ToArray();
        nodes.Add(Node("batch-impact-analysis", "BatchImpactAnalysis", "narrative_planning", TaskExecutionKind.Kernel,
            summaries, ["ContinuitySummary"], ["BatchImpactReport"]));
        nodes.Add(Node("acceptance-gate", BookProductionWorkflow.AcceptanceGate, "workflow", TaskExecutionKind.HumanGate,
            ["batch-impact-analysis"], ["BatchImpactReport"], ["AcceptanceDecision"]));
        nodes.Add(Node("prefix-merge", "PrefixMerge", "domain_reducer", TaskExecutionKind.System,
            ["acceptance-gate"], ["AcceptanceDecision"], ["MergeRecord"], AuthorityMutation.MergeAcceptedPrefix));
        return nodes;
    }

    private static TaskGraphNode Node(
        string id,
        string taskType,
        string kernel,
        TaskExecutionKind executionKind,
        IReadOnlyList<string> dependsOn,
        IReadOnlyList<string> requires,
        IReadOnlyList<string> produces,
        AuthorityMutation authorityMutation = AuthorityMutation.None,
        int? chapter = null) =>
        new(id, taskType, kernel, executionKind, dependsOn, requires, produces, authorityMutation, chapter);

    private sealed record ChapterRange(int Start, int End);
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Kernels;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Web.NovelAgentWeb.Services.Context;

public sealed class AgentContextAssembler : IAgentContextAssembler
{
    private readonly NovelAgentDbContext _db;
    private readonly IMemoryStore _memory;
    private readonly IKnowledgeQueryTool _knowledge;

    public AgentContextAssembler(
        NovelAgentDbContext db,
        IMemoryStore memory,
        IKnowledgeQueryTool knowledge)
    {
        _db = db;
        _memory = memory;
        _knowledge = knowledge;
    }

    public async Task<AgentContextEnvelope> BuildAsync(
        AgentContextRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Profile != AgentContextProfile.Conversation)
            throw new NotSupportedException($"上下文 Profile 尚未接入：{request.Profile}");

        var project = await _db.NovelProjects.AsNoTracking()
            .Where(item => item.Id == request.ProjectId && item.UserId == request.UserId)
            .Select(item => new AgentProjectContext(
                item.Id,
                item.Title,
                item.Genre ?? string.Empty,
                item.SubGenre ?? string.Empty,
                item.CoreHook ?? string.Empty,
                item.Status,
                item.WordCount,
                _db.Chapters.Count(chapter => chapter.ProjectId == item.Id)))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("项目不存在或不属于当前用户。");

        var latestGoal = await _db.CreativeGoals.AsNoTracking()
            .Where(item => item.UserId == request.UserId && item.ProjectId == request.ProjectId)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new AgentGoalContext(
                item.Id,
                item.Status,
                item.CollaborationMode,
                item.HumanReadableObjective))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var memory = await _memory.ReadAsync(
                request.UserId,
                request.ProjectId,
                request.SessionId,
                latestGoal?.Id,
                cancellationToken)
            .ConfigureAwait(false);
        var knowledge = await _knowledge.ExecuteAsync(new KnowledgeQueryRequest(
                KnowledgeQueryIntent.Retrieve,
                KnowledgeQueryScope.CurrentProject,
                request.ProjectId,
                request.Query,
                Limit: request.KnowledgeLimit), cancellationToken)
            .ConfigureAwait(false);
        var pendingIntents = await _db.CreativeIntents.AsNoTracking()
            .Where(item =>
                item.UserId == request.UserId &&
                item.ProjectId == request.ProjectId &&
                item.Status == "candidate" &&
                item.RequiresConfirmation)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new AgentPendingIntentContext(
                item.Id,
                item.Source,
                item.RawContent,
                item.NormalizedIntent,
                item.TargetScope,
                item.Status,
                item.MetadataJson))
            .Take(12)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var dialogue = new List<DialogueMessage>();
        if (!string.IsNullOrWhiteSpace(memory.Structured.Chat.MetaSummary))
            dialogue.Add(new DialogueMessage("context", memory.Structured.Chat.MetaSummary));
        dialogue.AddRange(memory.Structured.Chat.RecentSummaries
            .Select(item => new DialogueMessage("context", item.Content)));
        dialogue.AddRange(memory.Structured.Chat.RecentMessages
            .Select(item => new DialogueMessage(item.Role, item.Content)));

        var sources = memory.Records
            .Select(item => new AgentContextSource(
                $"memory:{item.Scope}",
                item.Id,
                item.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Concat(knowledge.Context.Items.Select(item => new AgentContextSource(
                "knowledge",
                item.Id,
                knowledge.Context.KnowledgeVersion)))
            .Concat(pendingIntents.Select(item => new AgentContextSource(
                $"intent:{item.Source}",
                item.Id,
                item.Status)))
            .Append(new AgentContextSource("project", project.Id, project.Status))
            .ToArray();

        return new AgentContextEnvelope(
            request.Profile,
            project,
            latestGoal,
            memory,
            knowledge.Context,
            pendingIntents,
            dialogue,
            sources);
    }

    public async Task<KernelExecutionContext> BuildKernelExecutionAsync(
        KernelTaskClaim claim,
        CancellationToken cancellationToken = default)
    {
        var task = await _db.KernelTasks.AsNoTracking().SingleAsync(item =>
            item.Id == claim.TaskId &&
            item.UserId == claim.UserId &&
            item.TaskGraphVersionId == claim.TaskGraphVersionId,
            cancellationToken);
        var graphTasks = await _db.KernelTasks.AsNoTracking()
            .Where(item => item.UserId == claim.UserId && item.TaskGraphVersionId == claim.TaskGraphVersionId)
            .ToListAsync(cancellationToken);
        var byNodeId = graphTasks.ToDictionary(TaskNodeId, StringComparer.Ordinal);
        var dependencyNodeIds = JsonSerializer.Deserialize<string[]>(task.DependencyTaskIdsJson) ?? [];
        var artifactIds = new List<string>(JsonSerializer.Deserialize<string[]>(task.InputArtifactIdsJson) ?? []);
        foreach (var dependencyNodeId in dependencyNodeIds)
        {
            if (!byNodeId.TryGetValue(dependencyNodeId, out var dependency))
                throw new InvalidOperationException($"依赖任务不存在：{dependencyNodeId}");
            if (dependency.Status is not ("completed" or "reused"))
                throw new InvalidOperationException($"依赖任务尚未完成：{dependencyNodeId}");
            artifactIds.AddRange(JsonSerializer.Deserialize<string[]>(dependency.OutputArtifactIdsJson) ?? []);
        }

        artifactIds = artifactIds.Distinct(StringComparer.Ordinal).ToList();
        var inputArtifacts = artifactIds.Count == 0
            ? []
            : await _db.KernelArtifacts.AsNoTracking()
                .Where(artifact => artifact.UserId == claim.UserId && artifactIds.Contains(artifact.Id))
                .Select(artifact => new KernelInputArtifact(
                    artifact.Id,
                    artifact.ArtifactType,
                    artifact.SchemaVersion,
                    artifact.ContentJson,
                    artifact.ContentHash,
                    artifact.Authorship,
                    artifact.IsProtected))
                .ToListAsync(cancellationToken);
        if (inputArtifacts.Count != artifactIds.Count)
            throw new InvalidOperationException("依赖 Artifact 缺失或不属于当前用户。");

        var snapshot = await _db.GoalContextSnapshots.AsNoTracking().SingleAsync(item =>
            item.UserId == claim.UserId && item.GoalId == claim.GoalId,
            cancellationToken);
        var goalContract = await LoadGoalContractAsync(claim, cancellationToken).ConfigureAwait(false);
        return new KernelExecutionContext(claim, snapshot, goalContract, inputArtifacts);
    }

    private async Task<KernelGoalContract> LoadGoalContractAsync(
        KernelTaskClaim claim,
        CancellationToken cancellationToken)
    {
        var graph = await _db.TaskGraphVersions.AsNoTracking().SingleAsync(item =>
            item.Id == claim.TaskGraphVersionId &&
            item.UserId == claim.UserId &&
            item.GoalId == claim.GoalId,
            cancellationToken);
        var committedGoal = await _db.CreativeGoals.AsNoTracking().SingleAsync(item =>
            item.Id == claim.GoalId && item.UserId == claim.UserId,
            cancellationToken);
        int? targetRevisionNumber = null;
        if (graph.GoalRevisionId != null)
        {
            targetRevisionNumber = await _db.GoalRevisions.AsNoTracking()
                .Where(item =>
                    item.Id == graph.GoalRevisionId &&
                    item.GoalId == claim.GoalId &&
                    item.UserId == claim.UserId)
                .Select(item => (int?)item.RevisionNumber)
                .SingleAsync(cancellationToken)
                ?? throw new InvalidOperationException("任务图关联的 Goal Revision 缺少版本号。");
        }
        var revisions = targetRevisionNumber.HasValue
            ? await _db.GoalRevisions.AsNoTracking()
                .Where(item =>
                    item.GoalId == claim.GoalId &&
                    item.UserId == claim.UserId &&
                    item.RevisionNumber <= targetRevisionNumber.Value)
                .OrderBy(item => item.RevisionNumber)
                .ToListAsync(cancellationToken)
            : [];
        var goal = CreativeGoalRevisionProjector.Project(committedGoal, revisions);
        return new KernelGoalContract(
            goal.GoalType,
            goal.CollaborationMode,
            goal.HumanReadableObjective,
            goal.TargetChapterRangeJson,
            goal.SuccessCriteriaJson,
            goal.MustPreserveJson,
            goal.MustHappenJson,
            goal.MustNotChangeJson,
            goal.AcceptancePolicyJson,
            goal.ReworkPolicyJson);
    }

    private static string TaskNodeId(KernelTask task)
    {
        var separator = task.Id.IndexOf(':');
        return separator < 0 ? task.Id : task.Id[(separator + 1)..];
    }
}

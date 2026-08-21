using System.Security.Cryptography;
using System.Text;
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
        var snapshot = await BuildProjectSnapshotAsync(
                new ProjectContextSnapshotRequest(
                    request.UserId,
                    request.ProjectId,
                    request.SessionId,
                    null,
                    request.Query,
                    request.KnowledgeLimit),
                cancellationToken)
            .ConfigureAwait(false);

        var dialogue = new List<DialogueMessage>();
        if (!string.IsNullOrWhiteSpace(snapshot.Memory.Structured.Chat.MetaSummary))
            dialogue.Add(new DialogueMessage("context", snapshot.Memory.Structured.Chat.MetaSummary));
        dialogue.AddRange(snapshot.Memory.Structured.Chat.RecentSummaries
            .Select(item => new DialogueMessage("context", item.Content)));
        dialogue.AddRange(snapshot.Memory.Structured.Chat.RecentMessages
            .Select(item => new DialogueMessage(item.Role, item.Content)));

        return new AgentContextEnvelope(
            request.Profile,
            snapshot.Project,
            snapshot.LatestGoal,
            snapshot.Memory,
            snapshot.Knowledge,
            snapshot.PendingIntents,
            dialogue,
            snapshot.Sources);
    }

    public async Task<ProjectContextSnapshot> BuildProjectSnapshotAsync(
        ProjectContextSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureProjectScope(request.UserId, request.ProjectId, request.SessionId);

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
                _db.Chapters.Count(chapter => chapter.ProjectId == item.Id),
                item.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("项目不存在或不属于当前用户。");

        var goalQuery = _db.CreativeGoals.AsNoTracking()
            .Where(item => item.UserId == request.UserId && item.ProjectId == request.ProjectId);
        if (!string.IsNullOrWhiteSpace(request.GoalId))
            goalQuery = goalQuery.Where(item => item.Id == request.GoalId);

        var latestGoal = await goalQuery
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new AgentGoalContext(
                item.Id,
                item.Status,
                item.CollaborationMode,
                item.HumanReadableObjective,
                item.AggregateVersion))
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
                item.MetadataJson,
                item.UpdatedAt))
            .Take(12)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

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
                item.UpdatedAt.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture))))
            .Append(new AgentContextSource(
                "project",
                project.Id,
                project.UpdatedAt.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        if (!string.IsNullOrWhiteSpace(request.BindingVersion))
        {
            sources.Add(new AgentContextSource(
                "binding",
                request.SessionId,
                request.BindingVersion));
        }
        if (latestGoal != null)
        {
            sources.Add(new AgentContextSource(
                "goal",
                latestGoal.Id,
                latestGoal.AggregateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var allowedTools = new[]
        {
            _knowledge.Name,
            "confirm_creative_goal"
        };
        return new ProjectContextSnapshot(
            CalculateContextVersion(sources),
            project,
            latestGoal,
            memory,
            knowledge.Context,
            pendingIntents,
            allowedTools,
            sources);
    }

    public async Task<KernelExecutionContext> BuildKernelExecutionAsync(
        KernelTaskClaim claim,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(claim.ProjectId))
            throw new InvalidOperationException("Kernel execution requires a project id.");

        var task = await _db.KernelTasks.AsNoTracking().SingleAsync(item =>
            item.Id == claim.TaskId &&
            item.UserId == claim.UserId &&
            item.ProjectId == claim.ProjectId &&
            item.TaskGraphVersionId == claim.TaskGraphVersionId,
            cancellationToken);
        var graphTasks = await _db.KernelTasks.AsNoTracking()
            .Where(item =>
                item.UserId == claim.UserId &&
                item.ProjectId == claim.ProjectId &&
                item.TaskGraphVersionId == claim.TaskGraphVersionId)
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
                .Where(artifact =>
                    artifact.UserId == claim.UserId &&
                    artifact.ProjectId == claim.ProjectId &&
                    artifactIds.Contains(artifact.Id))
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
            item.UserId == claim.UserId &&
            item.ProjectId == claim.ProjectId &&
            item.GoalId == claim.GoalId,
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
            item.ProjectId == claim.ProjectId &&
            item.GoalId == claim.GoalId,
            cancellationToken);
        var committedGoal = await _db.CreativeGoals.AsNoTracking().SingleAsync(item =>
            item.Id == claim.GoalId &&
            item.UserId == claim.UserId &&
            item.ProjectId == claim.ProjectId,
            cancellationToken);
        int? targetRevisionNumber = null;
        if (graph.GoalRevisionId != null)
        {
            targetRevisionNumber = await _db.GoalRevisions.AsNoTracking()
                .Where(item =>
                    item.Id == graph.GoalRevisionId &&
                    item.GoalId == claim.GoalId &&
                    item.UserId == claim.UserId &&
                    item.ProjectId == claim.ProjectId)
                .Select(item => (int?)item.RevisionNumber)
                .SingleAsync(cancellationToken)
                ?? throw new InvalidOperationException("任务图关联的 Goal Revision 缺少版本号。");
        }
        var revisions = targetRevisionNumber.HasValue
            ? await _db.GoalRevisions.AsNoTracking()
                .Where(item =>
                    item.GoalId == claim.GoalId &&
                    item.UserId == claim.UserId &&
                    item.ProjectId == claim.ProjectId &&
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

    private static string CalculateContextVersion(IReadOnlyList<AgentContextSource> sources)
    {
        var material = string.Join(
            '\n',
            sources
                .OrderBy(item => item.SourceType, StringComparer.Ordinal)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal)
                .Select(item => $"{item.SourceType}:{item.SourceId}:{item.Version}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return $"ctx-{hash[..24].ToLowerInvariant()}";
    }

    private static void EnsureProjectScope(string userId, string projectId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("A user id is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("A project id is required.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("A session id is required.", nameof(sessionId));
    }

    private static string TaskNodeId(KernelTask task)
    {
        var separator = task.Id.IndexOf(':');
        return separator < 0 ? task.Id : task.Id[(separator + 1)..];
    }
}

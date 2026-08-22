using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using Tianming.NovelAgent.Application.Ports;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Kernels;

namespace TM.Web.NovelAgentWeb.Services.Rework;

public sealed record ReworkGraphCompileRequest(
    string GoalId,
    int ChapterNumber,
    string CandidateChapterId,
    int CandidateVersion,
    string SessionId,
    string UserDescription,
    int? SelectionStart,
    int? SelectionEnd,
    string? SelectedText,
    string IdempotencyKey);

public sealed record ReworkGraphCompileResult(
    string TaskId,
    string IntentArtifactId,
    string Status);

public interface IReworkGraphCompiler
{
    Task<ReworkGraphCompileResult> CompileAsync(
        ReworkGraphCompileRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ReworkGraphCompiler : IReworkGraphCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IReworkIntentService _intents;
    private readonly ILegacyControlPlaneCommands _controlPlane;

    public ReworkGraphCompiler(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        IReworkIntentService intents,
        ILegacyControlPlaneCommands controlPlane)
    {
        _db = db;
        _currentUser = currentUser;
        _intents = intents;
        _controlPlane = controlPlane;
    }

    public async Task<ReworkGraphCompileResult> CompileAsync(
        ReworkGraphCompileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("返工请求必须提供幂等键。", nameof(request));
        var userId = _currentUser.GetUserId();
        var goal = await _db.CreativeGoals.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == request.GoalId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
        var taskKey = BuildIdempotencyKey(goal.Id, request.IdempotencyKey);
        await AcquireLockAsync(userId, taskKey, cancellationToken);
        var existing = await _db.KernelTasks.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == userId && item.IdempotencyKey == taskKey,
            cancellationToken);
        if (existing != null)
            return new(existing.Id, LastArtifactId(existing.InputArtifactIdsJson), existing.Status);

        var candidate = await _db.CandidateChapters.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == request.CandidateChapterId &&
            item.UserId == userId &&
            item.ProjectId == goal.ProjectId &&
            item.GoalId == goal.Id &&
            item.ChapterNumber == request.ChapterNumber &&
            item.Version == request.CandidateVersion,
            cancellationToken) ?? throw new KeyNotFoundException("候选章节不存在、版本不匹配或不属于当前用户。");
        var graph = await _db.TaskGraphVersions
            .Where(item => item.UserId == userId && item.ProjectId == goal.ProjectId && item.GoalId == goal.Id && item.Status == "active")
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Goal 尚未编译活动任务图。");
        var contextArtifactId = await _db.KernelArtifacts.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.ProjectId == goal.ProjectId &&
                item.GoalId == goal.Id &&
                item.BranchId == candidate.BranchId &&
                item.TaskId == $"{graph.Id}:chapter-{request.ChapterNumber}-context" &&
                item.ArtifactType == "ChapterContextContract")
            .Select(item => item.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("返工缺少该章冻结的 ChapterContextContract。");
        var originalExists = await _db.KernelArtifacts.AsNoTracking().AnyAsync(item =>
            item.Id == candidate.CurrentArtifactId && item.UserId == userId && item.GoalId == goal.Id && item.BranchId == candidate.BranchId,
            cancellationToken);
        if (!originalExists)
            throw new KeyNotFoundException("候选正文 Artifact 不存在。");

        var intent = await _intents.CompileAsync(new CompileReworkIntentRequest(
            candidate.Id,
            candidate.Version,
            request.SessionId,
            request.UserDescription,
            request.SelectionStart,
            request.SelectionEnd,
            request.SelectedText ?? string.Empty), cancellationToken);
        await _intents.StartAutomaticAttemptAsync(intent.Id, cancellationToken);
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

        var prefix = $"manual-rework-{Sha256(taskKey)[..16]}";
        var nodes = BuildNodes(prefix, request.ChapterNumber);
        ValidateNodes(nodes);
        var now = DateTime.UtcNow;
        var definition = JsonSerializer.Deserialize<TaskGraphDefinition>(graph.GraphJson, JsonOptions)
            ?? throw new InvalidOperationException("活动任务图内容无效。");
        var updatedDefinition = definition with { Nodes = definition.Nodes.Concat(nodes).ToArray() };
        var updatedGraphJson = JsonSerializer.Serialize(updatedDefinition, JsonOptions);
        var updatedHash = Sha256(updatedGraphJson);
        var draftNodeId = nodes[0].Id;
        var draftTaskId = $"{graph.Id}:{draftNodeId}";
        var intentArtifactId = Guid.NewGuid().ToString("N");
        var intentContentHash = Sha256(intentJson);
        var draftInputArtifactIdsJson = JsonSerializer.Serialize(
            new[] { contextArtifactId, candidate.CurrentArtifactId, intentArtifactId }, JsonOptions);

        var tasks = nodes.Select((node, index) => new LegacyReworkTask(
            $"{graph.Id}:{node.Id}",
            node.TaskType,
            node.KernelName,
            node.DependsOn.Count == 0 ? "ready" : "blocked",
            JsonSerializer.Serialize(node.DependsOn, JsonOptions),
            node.Id.EndsWith("-draft", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new[] { contextArtifactId, candidate.CurrentArtifactId }, JsonOptions)
                : node.Id.EndsWith("-adopt", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(new[] { contextArtifactId }, JsonOptions)
                    : "[]",
            "[]",
            index switch
            {
                0 => taskKey,
                1 => $"{taskKey}:continuity",
                2 => $"{taskKey}:literary",
                3 => $"{taskKey}:adopt",
                4 => $"{taskKey}:summary",
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            },
            KernelTaskFailurePolicy.MaxAttempts(node.TaskType),
            100 + index)).ToList();

        // Control-plane persistence is owned by the Application command: it
        // updates the active graph, wires the draft task inputs, inserts the
        // intent artifact and appends the rework tasks in one transaction.
        var draftStatus = tasks[0].Status;
        try
        {
            await _controlPlane.PersistReworkGraphAsync(new LegacyReworkGraphCommand(
            userId,
            goal.ProjectId,
            goal.Id,
            graph.Id,
            updatedGraphJson,
            updatedHash,
            draftTaskId,
            draftInputArtifactIdsJson,
            new LegacyReworkGraphIntentArtifact(
                intentArtifactId,
                draftTaskId,
                candidate.BranchId,
                "ReworkIntent",
                1,
                intentJson,
                intentContentHash,
                "adopted",
                "human"),
            tasks),
                cancellationToken);
        }
        catch
        {
            // The intent was committed by the rework-intent service before the
            // control-plane command ran on its own context; compensate so a
            // failed rework leaves no dangling executing attempt.
            // Drop the failed control-plane batch from the tracker before
            // compensating, otherwise SaveChanges replays the conflict.
            _db.ChangeTracker.Clear();
            var committedIntent = await _db.ReworkIntents
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == intent.Id, cancellationToken);
            if (committedIntent is not null)
            {
                _db.ReworkIntents.Remove(committedIntent);
                await _db.SaveChangesAsync(cancellationToken);
            }
            throw;
        }

        return new(draftTaskId, intentArtifactId, draftStatus);
    }

    private static TaskGraphNode[] BuildNodes(string prefix, int chapterNumber)
    {
        var draft = $"{prefix}-draft";
        var continuity = $"{prefix}-continuity-review";
        var literary = $"{prefix}-literary-review";
        var adopt = $"{prefix}-adopt";
        return
        [
            new(draft, "DirectedReworkDraft", "tianming_writing", TaskExecutionKind.Kernel, [], [], ["CandidateChapterDraft"], AuthorityMutation.None, chapterNumber),
            new(continuity, "ReviewContinuity", "continuity_review", TaskExecutionKind.Kernel, [draft], ["CandidateChapterDraft"], ["ContinuityReview"], AuthorityMutation.None, chapterNumber),
            new(literary, "ReviewLiteraryQuality", "literary_review", TaskExecutionKind.Kernel, [draft], ["CandidateChapterDraft"], ["LiteraryReview"], AuthorityMutation.None, chapterNumber),
            new(adopt, "DirectedRework", "tianming_writing", TaskExecutionKind.Kernel, [draft, continuity, literary], ["CandidateChapterDraft", "ContinuityReview", "LiteraryReview"], ["ReviewedCandidateChapter"], AuthorityMutation.None, chapterNumber),
            new($"{prefix}-continuity-summary", "ExtractContinuitySummary", "continuity_review", TaskExecutionKind.Kernel, [adopt], ["ReviewedCandidateChapter"], ["ContinuitySummary"], AuthorityMutation.None, chapterNumber)
        ];
    }

    private static void ValidateNodes(IReadOnlyList<TaskGraphNode> nodes)
    {
        var ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != nodes.Count || nodes.SelectMany(node => node.DependsOn).Any(dependency => !ids.Contains(dependency)))
            throw new InvalidOperationException("返工子图包含重复节点或缺失依赖。");
        if (nodes.Any(node => node.ExecutionKind != TaskExecutionKind.Kernel || node.AuthorityMutation != AuthorityMutation.None))
            throw new InvalidOperationException("返工子图不能修改权威状态。");
    }

    private async Task AcquireLockAsync(string userId, string key, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational() || _db.Database.GetDbConnection() is not Npgsql.NpgsqlConnection)
            return;
        var lockKey = $"goal-rework\u001f{userId}\u001f{key}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }

    private static IReadOnlyList<string> DeserializeList(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
    private static string BuildIdempotencyKey(string goalId, string requestKey)
    {
        var raw = $"goal-rework:{goalId}:{requestKey.Trim()}";
        return raw.Length <= 160 ? raw : $"goal-rework:{Sha256(raw)}";
    }
    private static string LastArtifactId(string json) => (JsonSerializer.Deserialize<string[]>(json) ?? []).LastOrDefault() ?? string.Empty;
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

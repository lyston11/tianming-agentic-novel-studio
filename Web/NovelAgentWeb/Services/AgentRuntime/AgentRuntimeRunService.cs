using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public sealed class AgentRuntimeRunService : IAgentRuntimeRunService
{
    private static readonly TimeSpan ActiveRunCacheTtl = TimeSpan.FromMinutes(30);
    private readonly NovelAgentDbContext _db;
    private readonly IDistributedCacheService? _redis;

    public AgentRuntimeRunService(NovelAgentDbContext db, IDistributedCacheService? redis = null)
    {
        _db = db;
        _redis = redis;
    }

    public async Task<AgentRuntimeRun> CreateQueuedAsync(CreateAgentRuntimeRunRequest request, CancellationToken ct = default)
    {
        var idempotencyKey = Normalize(request.IdempotencyKey);
        if (idempotencyKey != string.Empty)
        {
            var existing = await _db.AgentRuntimeRuns
                .FirstOrDefaultAsync(r =>
                    r.UserId == request.UserId &&
                    r.SessionId == request.SessionId &&
                    r.IdempotencyKey == idempotencyKey,
                    ct)
                .ConfigureAwait(false);
            if (existing != null)
            {
                await CacheActiveRunAsync(existing, ct).ConfigureAwait(false);
                return existing;
            }
        }

        var now = DateTime.UtcNow;
        var projectId = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId;
        var run = new AgentRuntimeRun
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            SessionId = request.SessionId,
            ProjectId = projectId,
            LockedProjectId = projectId, // 创建时锁定 ProjectId，运行中不可变更
            ExecutedToolsJson = "[]",
            Status = AgentRuntimeRunStatus.Queued,
            Mode = NormalizeMode(request.Mode),
            CurrentPhase = AgentRuntimeRunStatus.Queued,
            UserMessage = request.UserMessage.Trim(),
            SourceMessageId = Normalize(request.SourceMessageId),
            IdempotencyKey = idempotencyKey,
            BudgetJson = Serialize(request.Budget),
            LastMessage = "已进入后台执行队列。",
            ResultJson = "{}",
            ErrorMessage = string.Empty,
            FailureJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.AgentRuntimeRuns.Add(run);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await CacheActiveRunAsync(run, ct).ConfigureAwait(false);
        return run;
    }

    public Task<AgentRuntimeRun?> TryGetAsync(string runtimeRunId, CancellationToken ct = default) =>
        _db.AgentRuntimeRuns.FirstOrDefaultAsync(r => r.Id == runtimeRunId, ct);

    public async Task<AgentRuntimeRun?> TryGetActiveAsync(string userId, string sessionId, CancellationToken ct = default)
    {
        var state = await TryGetActiveStateAsync(userId, sessionId, ct).ConfigureAwait(false);
        return state?.Run;
    }

    public async Task<AgentRuntimeActiveRunState?> TryGetActiveStateAsync(
        string userId,
        string sessionId,
        CancellationToken ct = default)
    {
        var activeKey = BuildActiveRunKey(userId, sessionId);
        var snapshot = _redis == null
            ? null
            : await _redis.GetAsync<AgentRuntimeRunCacheSnapshot>(activeKey, ct).ConfigureAwait(false);
        if (snapshot != null)
        {
            var cachedRun = await TryHydrateActiveSnapshotAsync(snapshot, userId, sessionId, ct).ConfigureAwait(false);
            if (cachedRun != null)
                return new AgentRuntimeActiveRunState(cachedRun, snapshot.HeartbeatAt, FromDistributedCache: true);

            await ClearActiveRunCacheAsync(userId, sessionId, snapshot.RuntimeRunId, ct).ConfigureAwait(false);
        }

        var run = await _db.AgentRuntimeRuns
            .Where(r => r.UserId == userId &&
                        r.SessionId == sessionId &&
                        AgentRuntimeRunStatus.Active.Contains(r.Status))
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (run != null)
            await CacheActiveRunAsync(run, ct).ConfigureAwait(false);
        return run == null
            ? null
            : new AgentRuntimeActiveRunState(run, run.UpdatedAt, FromDistributedCache: false);
    }

    public async Task<IReadOnlyList<AgentRuntimeActiveSessionCursor>> ListActiveSessionCursorsAsync(
        int limit = 100,
        CancellationToken ct = default)
    {
        var candidateLimit = Math.Clamp(limit, 1, 500) * 3;
        var activeRuns = await _db.AgentRuntimeRuns
            .AsNoTracking()
            .Where(r => AgentRuntimeRunStatus.Active.Contains(r.Status))
            .OrderByDescending(r => r.UpdatedAt)
            .Take(candidateLimit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return activeRuns
            .GroupBy(r => new { r.UserId, r.SessionId })
            .Select(g => g.OrderByDescending(r => r.UpdatedAt).First())
            .OrderByDescending(r => r.UpdatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(r => new AgentRuntimeActiveSessionCursor(
                r.Id,
                r.UserId,
                r.SessionId,
                r.ProjectId,
                r.UpdatedAt))
            .ToList();
    }

    public async Task<AgentRuntimeRun> MarkRunningAsync(string runtimeRunId, CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        run.Status = AgentRuntimeRunStatus.Running;
        run.CurrentPhase = AgentRuntimeRunStatus.Running;
        run.StartedAt ??= DateTime.UtcNow;
        run.UpdatedAt = DateTime.UtcNow;
        run.LastMessage = "Agent 已开始后台执行。";
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await CacheActiveRunAsync(run, ct).ConfigureAwait(false);
        return run;
    }

    public async Task<AgentRuntimeRun> UpdateProgressAsync(
        string runtimeRunId,
        string phase,
        string message,
        string? activeTool = null,
        int? currentStep = null,
        CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        run.CurrentPhase = string.IsNullOrWhiteSpace(phase) ? run.CurrentPhase : phase;
        run.LastMessage = string.IsNullOrWhiteSpace(message) ? run.LastMessage : message.Trim();
        run.ActiveTool = activeTool ?? run.ActiveTool;
        if (IsProductionToolOrStage(activeTool) || IsProductionToolOrStage(phase))
            run.Mode = AgentRuntimeRunMode.Production;
        if (currentStep.HasValue)
            run.CurrentStep = currentStep.Value;
        run.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await CacheActiveRunAsync(run, ct).ConfigureAwait(false);
        return run;
    }

    public async Task<AgentRuntimeRun> MarkCompletedAsync(string runtimeRunId, object? result, CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        run.Status = AgentRuntimeRunStatus.Completed;
        run.CurrentPhase = AgentRuntimeRunStatus.Completed;
        run.ResultJson = Serialize(result);
        run.LastMessage = BuildCompletedMessage(result);
        run.CompletedAt = DateTime.UtcNow;
        run.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ClearActiveRunCacheAsync(run, ct).ConfigureAwait(false);
        return run;
    }

    public async Task<AgentRuntimeRun> MarkFailedAsync(string runtimeRunId, string errorMessage, CancellationToken ct = default)
    {
        return await MarkFailedAsync(
            runtimeRunId,
            new AgentRuntimeFailure(
                Code: "RUNTIME_FAILED",
                Stage: AgentRuntimeRunStatus.Failed,
                Message: errorMessage,
                Recoverable: false),
            ct).ConfigureAwait(false);
    }

    public async Task<AgentRuntimeRun> MarkFailedAsync(string runtimeRunId, AgentRuntimeFailure failure, CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        run.Status = AgentRuntimeRunStatus.Failed;
        run.CurrentPhase = string.IsNullOrWhiteSpace(failure.Stage) ? AgentRuntimeRunStatus.Failed : failure.Stage.Trim();
        run.ErrorMessage = failure.Message;
        run.FailureJson = Serialize(new
        {
            code = failure.Code,
            stage = failure.Stage,
            message = failure.Message,
            recoverable = failure.Recoverable,
            recommendedAction = failure.RecommendedAction,
            artifactIds = failure.ArtifactIds ?? Array.Empty<string>()
        });
        run.LastMessage = "后台执行失败。";
        run.CompletedAt = DateTime.UtcNow;
        run.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ClearActiveRunCacheAsync(run, ct).ConfigureAwait(false);
        return run;
    }

    public async Task<AgentRuntimeRun> MarkCancelledAsync(
        string runtimeRunId,
        string stage,
        string message,
        CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        var normalizedStage = string.IsNullOrWhiteSpace(stage) ? AgentRuntimeRunStatus.Cancelled : stage.Trim();
        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "后台执行已取消。" : message.Trim();
        run.Status = AgentRuntimeRunStatus.Cancelled;
        run.CurrentPhase = normalizedStage;
        run.ErrorMessage = string.Empty;
        run.FailureJson = Serialize(new
        {
            code = "RUNTIME_CANCELLED",
            stage = normalizedStage,
            message = normalizedMessage,
            recoverable = true,
            recommendedAction = "用户取消了本次后台执行，可基于当前工作流状态继续或重新发起任务。",
            artifactIds = Array.Empty<string>()
        });
        run.LastMessage = normalizedMessage;
        run.CompletedAt = DateTime.UtcNow;
        run.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ClearActiveRunCacheAsync(run, ct).ConfigureAwait(false);
        return run;
    }

    public async Task<AgentRuntimeRun> RequestCancelAsync(string runtimeRunId, CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        run.CancelRequested = true;
        run.LastMessage = "已收到暂停/取消请求，Agent 会在安全边界处理。";
        run.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await CacheActiveRunAsync(run, ct).ConfigureAwait(false);
        return run;
    }

    public async Task<int> FailStaleActiveRunsAsync(
        TimeSpan staleAfter,
        string reason,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.Subtract(staleAfter <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : staleAfter);
        var runs = await _db.AgentRuntimeRuns
            .Where(r => AgentRuntimeRunStatus.Active.Contains(r.Status) && r.UpdatedAt <= cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (runs.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        foreach (var run in runs)
        {
            run.Status = AgentRuntimeRunStatus.Failed;
            run.CurrentPhase = "heartbeat_lost";
            run.ErrorMessage = string.IsNullOrWhiteSpace(reason)
                ? "后台运行心跳超时，已从数据库 truth source 标记为可恢复失败。"
                : reason.Trim();
            run.FailureJson = Serialize(new
            {
                code = "RUNTIME_HEARTBEAT_LOST",
                stage = "heartbeat_lost",
                message = run.ErrorMessage,
                recoverable = true,
                recommendedAction = "QueryRuntimeRun 后按当前章节、工作流和工具结果决定恢复、重试或询问用户。",
                artifactIds = Array.Empty<string>()
            });
            run.LastMessage = "后台运行心跳超时，已进入可恢复失败状态。";
            run.CompletedAt = now;
            run.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var run in runs)
            await ClearActiveRunCacheAsync(run, ct).ConfigureAwait(false);
        return runs.Count;
    }

    private async Task<AgentRuntimeRun> RequireRunAsync(string runtimeRunId, CancellationToken ct)
    {
        var run = await TryGetAsync(runtimeRunId, ct).ConfigureAwait(false);
        return run ?? throw new KeyNotFoundException($"Runtime run {runtimeRunId} not found.");
    }

    private static string Serialize(object? value) =>
        value == null
            ? "{}"
            : JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeMode(string? mode)
    {
        var normalized = Normalize(mode);
        return normalized is AgentRuntimeRunMode.Answer or AgentRuntimeRunMode.Inspect or AgentRuntimeRunMode.Production
            ? normalized
            : AgentRuntimeRunMode.Inspect;
    }

    private static bool IsProductionToolOrStage(string? value)
    {
        var normalized = Normalize(value);
        return normalized is
            "ProduceChapter" or
            "ReviewChapter" or
            "ReviseCommittedChapter" or
            "RefreshProjectIndexes" or
            "AnalyzeDependencyImpact" or
            NovelAgentProductionStages.ProjectResolved or
            NovelAgentProductionStages.KnowledgeResolved or
            NovelAgentProductionStages.KnowledgeClassified or
            NovelAgentProductionStages.StoryDesignBuilt or
            NovelAgentProductionStages.VolumePlanBuilt or
            NovelAgentProductionStages.ChapterBlueprintBuilt or
            NovelAgentProductionStages.PackageBuilt or
            NovelAgentProductionStages.DraftGenerated or
            NovelAgentProductionStages.ChangesExtracted or
            NovelAgentProductionStages.GateValidated or
            NovelAgentProductionStages.DraftRewritten or
            NovelAgentProductionStages.ReviewCompleted or
            NovelAgentProductionStages.ChapterCommitted or
            NovelAgentProductionStages.FactsPersisted or
            NovelAgentProductionStages.IndexUpdated or
            NovelAgentProductionStages.RunCompleted or
            NovelAgentProductionStages.ContextPackage or
            NovelAgentProductionStages.DraftGeneration or
            NovelAgentProductionStages.GateValidation or
            NovelAgentProductionStages.DraftRepair or
            NovelAgentProductionStages.QualityReview or
            NovelAgentProductionStages.AwaitUserReview or
            NovelAgentProductionStages.ChapterCommit or
            NovelAgentProductionStages.GateValidationOrRepair;
    }

    private static string BuildCompletedMessage(object? result)
    {
        if (result is AgentChatResponse response)
        {
            var reply = string.IsNullOrWhiteSpace(response.Reply) ? string.Empty : response.Reply.Trim();
            if (reply.Length > 120)
                reply = reply[..120] + "...";
            return string.IsNullOrWhiteSpace(reply)
                ? "本轮执行已完成。"
                : $"本轮执行已完成：{reply}";
        }

        return "后台执行已完成。";
    }

    private async Task CacheActiveRunAsync(AgentRuntimeRun run, CancellationToken ct)
    {
        if (_redis == null || !AgentRuntimeRunStatus.Active.Contains(run.Status))
            return;

        var snapshot = BuildCacheSnapshot(run);
        await _redis.SetAsync(BuildActiveRunKey(run.UserId, run.SessionId), snapshot, ActiveRunCacheTtl, ct)
            .ConfigureAwait(false);
        await _redis.SetAsync(BuildHeartbeatKey(run.Id), snapshot, ActiveRunCacheTtl, ct)
            .ConfigureAwait(false);
    }

    private async Task ClearActiveRunCacheAsync(AgentRuntimeRun run, CancellationToken ct)
    {
        await ClearActiveRunCacheAsync(run.UserId, run.SessionId, run.Id, ct).ConfigureAwait(false);
    }

    private async Task ClearActiveRunCacheAsync(string userId, string sessionId, string runtimeRunId, CancellationToken ct)
    {
        if (_redis == null)
            return;

        await _redis.RemoveAsync(BuildActiveRunKey(userId, sessionId), ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(runtimeRunId))
            await _redis.RemoveAsync(BuildHeartbeatKey(runtimeRunId), ct).ConfigureAwait(false);
    }

    private async Task<AgentRuntimeRun?> TryHydrateActiveSnapshotAsync(
        AgentRuntimeRunCacheSnapshot snapshot,
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        if (!string.Equals(snapshot.UserId, userId, StringComparison.Ordinal) ||
            !string.Equals(snapshot.SessionId, sessionId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(snapshot.RuntimeRunId))
        {
            return null;
        }

        return await _db.AgentRuntimeRuns
            .FirstOrDefaultAsync(r =>
                    r.Id == snapshot.RuntimeRunId &&
                    r.UserId == userId &&
                    r.SessionId == sessionId &&
                    AgentRuntimeRunStatus.Active.Contains(r.Status),
                ct)
            .ConfigureAwait(false);
    }

    private static AgentRuntimeRunCacheSnapshot BuildCacheSnapshot(AgentRuntimeRun run)
    {
        var now = DateTime.UtcNow;
        return new AgentRuntimeRunCacheSnapshot(
            RuntimeRunId: run.Id,
            UserId: run.UserId,
            SessionId: run.SessionId,
            ProjectId: run.ProjectId,
            Status: run.Status,
            Mode: run.Mode,
            CurrentPhase: run.CurrentPhase,
            LastMessage: run.LastMessage,
            ActiveTool: run.ActiveTool,
            CurrentStep: run.CurrentStep,
            CancelRequested: run.CancelRequested,
            UpdatedAt: run.UpdatedAt,
            HeartbeatAt: now);
    }

    private static string BuildActiveRunKey(string userId, string sessionId) =>
        $"agent_runtime:active:{userId}:{sessionId}";

    private static string BuildHeartbeatKey(string runtimeRunId) =>
        $"agent_runtime:heartbeat:{runtimeRunId}";
}

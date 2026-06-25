using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public sealed class AgentToolExecutionLedger : IAgentToolExecutionLedger
{
    private static readonly TimeSpan RecentTtl = TimeSpan.FromMinutes(30);

    private readonly NovelAgentDbContext _db;
    private readonly IDistributedCacheService _redis;
    private readonly ILogger<AgentToolExecutionLedger> _logger;
    private readonly IAgentMemoryVersionService? _versions;

    public AgentToolExecutionLedger(
        NovelAgentDbContext db,
        IDistributedCacheService redis,
        ILogger<AgentToolExecutionLedger> logger,
        IAgentMemoryVersionService? versions = null)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
        _versions = versions;
    }

    public async Task<AgentToolExecution> StartAsync(AgentToolExecutionStart start, CancellationToken ct = default)
    {
        var argumentsJson = SerializeArguments(start.Call.Arguments);
        var execution = new AgentToolExecution
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = start.UserId,
            ProjectId = NullIfEmpty(start.ProjectId),
            SessionId = start.SessionId,
            RunId = NullIfEmpty(start.RunId),
            ToolName = start.Call.Name,
            Phase = start.Phase,
            Risk = start.Risk,
            ArgumentsJson = argumentsJson,
            ArgumentsHash = Sha256(argumentsJson),
            SideEffectsJson = JsonSerializer.Serialize(start.SideEffects ?? new AgentToolSideEffectSpec()),
            SemanticContractJson = SerializeSemanticContract(start.SemanticContract),
            Status = "running",
            StartedAt = DateTime.UtcNow
        };

        _db.AgentToolExecutions.Add(execution);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RefreshRecentAsync(execution.UserId, execution.SessionId, execution.ProjectId, ct).ConfigureAwait(false);
        return execution;
    }

    public async Task CompleteAsync(string executionId, AgentToolExecutionResult result, CancellationToken ct = default)
    {
        var execution = await _db.AgentToolExecutions
            .FirstOrDefaultAsync(x => x.Id == executionId, ct)
            .ConfigureAwait(false);
        if (execution == null)
            throw new KeyNotFoundException($"Tool execution {executionId} not found");

        var completedAt = DateTime.UtcNow;
        execution.Status = result.Success ? "succeeded" : "failed";
        execution.RunId = NullIfEmpty(result.RunId) ?? execution.RunId;
        execution.ResultPhase = result.Phase;
        execution.ResultMessage = result.Message;
        execution.ErrorType = result.Success ? string.Empty : InferErrorType(result);
        execution.ErrorMessage = result.Success ? string.Empty : result.Message;
        execution.FailureJson = SerializeFailure(result);
        execution.ArtifactJson = SerializeArtifact(result.Artifact);
        execution.RecommendedNextTool = result.RecommendedToolName;
        execution.MissingPrerequisite = result.MissingPrerequisite;
        execution.CompletedAt = completedAt;
        execution.DurationMs = Math.Max(0, (int)(completedAt - execution.StartedAt).TotalMilliseconds);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RefreshRecentAsync(execution.UserId, execution.SessionId, execution.ProjectId, ct).ConfigureAwait(false);
        await BumpToolExecutionVersionAsync(execution, ct).ConfigureAwait(false);
    }

    public async Task RebindProjectAsync(string executionId, string projectId, CancellationToken ct = default)
    {
        var normalizedProjectId = NullIfEmpty(projectId);
        if (normalizedProjectId == null)
            return;

        var execution = await _db.AgentToolExecutions
            .FirstOrDefaultAsync(x => x.Id == executionId, ct)
            .ConfigureAwait(false);
        if (execution == null)
            throw new KeyNotFoundException($"Tool execution {executionId} not found");

        var previousProjectId = execution.ProjectId;
        if (string.Equals(previousProjectId, normalizedProjectId, StringComparison.Ordinal))
            return;

        execution.ProjectId = normalizedProjectId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RefreshRecentAsync(execution.UserId, execution.SessionId, previousProjectId, ct).ConfigureAwait(false);
        await RefreshRecentAsync(execution.UserId, execution.SessionId, execution.ProjectId, ct).ConfigureAwait(false);
    }

    public async Task<int> FailRunningForSessionAsync(
        string userId,
        string sessionId,
        string? projectId,
        string reason,
        CancellationToken ct = default)
    {
        var normalizedProjectId = NullIfEmpty(projectId);
        var running = await _db.AgentToolExecutions
            .Where(x =>
                x.UserId == userId &&
                x.SessionId == sessionId &&
                x.ProjectId == normalizedProjectId &&
                x.Status == "running")
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return await FailRunningExecutionsAsync(running, reason, ct).ConfigureAwait(false);
    }

    public async Task<int> FailAllRunningAsync(string reason, CancellationToken ct = default)
    {
        var running = await _db.AgentToolExecutions
            .Where(x => x.Status == "running")
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return await FailRunningExecutionsAsync(running, reason, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentToolExecutionSnapshot>> GetRecentAsync(
        string userId,
        string sessionId,
        string? projectId,
        CancellationToken ct = default)
    {
        var normalizedProjectId = NullIfEmpty(projectId);
        var key = RecentKey(userId, sessionId, normalizedProjectId);
        try
        {
            var cached = await _redis.GetAsync<IReadOnlyList<AgentToolExecutionSnapshot>>(key, ct).ConfigureAwait(false);
            if (cached != null)
                return cached;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to read tool execution hot state for {CacheKey}", key);
        }

        var recent = await QueryRecentAsync(userId, sessionId, normalizedProjectId, ct).ConfigureAwait(false);
        try
        {
            await _redis.SetAsync<IReadOnlyList<AgentToolExecutionSnapshot>>(key, recent, RecentTtl, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to refresh tool execution hot state for {CacheKey}", key);
        }

        return recent;
    }

    private async Task<int> FailRunningExecutionsAsync(
        IReadOnlyList<AgentToolExecution> running,
        string reason,
        CancellationToken ct)
    {
        if (running.Count == 0)
            return 0;

        var completedAt = DateTime.UtcNow;
        foreach (var execution in running)
        {
            execution.Status = "failed";
            execution.ResultPhase = "runtime_cancelled";
            execution.ResultMessage = reason;
            execution.ErrorType = "runtime_cancelled";
            execution.ErrorMessage = reason;
            execution.FailureJson = SerializeFailure(new AgentToolExecutionResult
            {
                Success = false,
                Message = reason,
                Failure = new AgentToolFailure
                {
                    Code = "RUNTIME_CANCELLED",
                    FailedStage = "runtime_cancelled",
                    Reason = reason,
                    Recoverable = true,
                    RecommendedAction = "QueryRuntimeRun"
                }
            });
            execution.CompletedAt = completedAt;
            execution.DurationMs = Math.Max(0, (int)(completedAt - execution.StartedAt).TotalMilliseconds);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var affectedHotStates = running
            .Select(x => new { x.UserId, x.SessionId, x.ProjectId })
            .Distinct()
            .ToList();
        foreach (var state in affectedHotStates)
            await RefreshRecentAsync(state.UserId, state.SessionId, state.ProjectId, ct).ConfigureAwait(false);

        foreach (var execution in running)
            await BumpToolExecutionVersionAsync(execution, ct).ConfigureAwait(false);
        return running.Count;
    }

    private Task BumpToolExecutionVersionAsync(AgentToolExecution execution, CancellationToken ct) =>
        _versions == null
            ? Task.CompletedTask
            : _versions.BumpAsync(execution.UserId, execution.ProjectId, execution.SessionId, "tool_execution", ct);

    private async Task RefreshRecentAsync(string userId, string sessionId, string? projectId, CancellationToken ct)
    {
        var recent = await QueryRecentAsync(userId, sessionId, projectId, ct).ConfigureAwait(false);
        var key = RecentKey(userId, sessionId, projectId);
        try
        {
            await _redis.SetAsync<IReadOnlyList<AgentToolExecutionSnapshot>>(key, recent, RecentTtl, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to refresh tool execution hot state for {CacheKey}", key);
        }
    }

    private async Task<IReadOnlyList<AgentToolExecutionSnapshot>> QueryRecentAsync(
        string userId,
        string sessionId,
        string? projectId,
        CancellationToken ct) =>
        await _db.AgentToolExecutions
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.SessionId == sessionId && x.ProjectId == projectId)
            .OrderByDescending(x => x.StartedAt)
            .Take(20)
            .Select(x => new AgentToolExecutionSnapshot
            {
                Id = x.Id,
                ToolName = x.ToolName,
                Status = x.Status,
                RunId = x.RunId,
                Phase = x.Phase,
                ResultPhase = x.ResultPhase,
                ResultMessage = x.ResultMessage,
                RecommendedNextTool = x.RecommendedNextTool,
                StartedAt = x.StartedAt,
                CompletedAt = x.CompletedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public static string RecentKey(string userId, string sessionId, string? projectId) =>
        $"tool:recent:{userId}:{sessionId}:{(string.IsNullOrWhiteSpace(projectId) ? "*" : projectId)}";

    private static string SerializeArguments(Dictionary<string, string> arguments)
    {
        var sorted = arguments
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(sorted);
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string InferErrorType(AgentToolExecutionResult result)
    {
        if (result.Failure != null && !string.IsNullOrWhiteSpace(result.Failure.Code))
            return result.Failure.Code;
        if (result.IsRepairable)
            return "repairable";
        if (!string.IsNullOrWhiteSpace(result.MissingPrerequisite))
            return "missing_prerequisite";
        return "tool_failure";
    }

    private static string SerializeFailure(AgentToolExecutionResult result)
    {
        if (result.Success)
            return "{}";

        var failure = result.Failure ?? new AgentToolFailure
        {
            Code = InferErrorType(result),
            FailedStage = string.IsNullOrWhiteSpace(result.Phase) ? "tool_execution" : result.Phase,
            Reason = result.Message,
            Recoverable = result.IsRepairable,
            RecommendedAction = result.RecommendedToolName,
            ArtifactIds = result.Artifact == null || string.IsNullOrWhiteSpace(result.Artifact.ArtifactId)
                ? Array.Empty<string>()
                : new[] { result.Artifact.ArtifactId },
            RequiresUserDecision = result.RequiresConfirmation
        };

        NormalizeFailureFromResult(failure, result);
        return JsonSerializer.Serialize(failure, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static void NormalizeFailureFromResult(AgentToolFailure failure, AgentToolExecutionResult result)
    {
        if (string.IsNullOrWhiteSpace(failure.Code))
            failure.Code = InferErrorType(result);
        if (string.IsNullOrWhiteSpace(failure.FailedStage))
            failure.FailedStage = string.IsNullOrWhiteSpace(result.Phase) ? "tool_execution" : result.Phase;
        if (string.IsNullOrWhiteSpace(failure.Reason))
            failure.Reason = result.Message;
        if (!failure.Recoverable)
            failure.Recoverable = result.IsRepairable || result.Suggestions.Count > 0 || !string.IsNullOrWhiteSpace(result.RecommendedToolName);
        if (string.IsNullOrWhiteSpace(failure.RecommendedAction))
            failure.RecommendedAction = result.RecommendedToolName;
        if (failure.ArtifactIds.Count == 0 && result.Artifact != null && !string.IsNullOrWhiteSpace(result.Artifact.ArtifactId))
            failure.ArtifactIds = new[] { result.Artifact.ArtifactId };
        if (failure.ProducedArtifacts.Count == 0 && result.Artifact != null)
            failure.ProducedArtifacts = new[] { ToProducedArtifact(result.Artifact) };
        if (failure.InputArtifacts.Count == 0 && result.Data is ToolInputArtifactResolution resolution)
            failure.InputArtifacts = resolution.InputArtifacts;
        if (failure.RecoverableActions.Count == 0 && result.Suggestions.Count > 0)
            failure.RecoverableActions = result.Suggestions;
        if (result.RequiresConfirmation)
            failure.RequiresUserDecision = true;
    }

    private static AgentToolProducedArtifact ToProducedArtifact(AgentToolArtifact artifact) => new()
    {
        ArtifactType = artifact.ArtifactType,
        ArtifactId = artifact.ArtifactId,
        OutputKind = artifact.OutputKind,
        UserVisibleWhere = artifact.UserVisibleWhere,
        Summary = artifact.Summary
    };

    private static string SerializeArtifact(AgentToolArtifact? artifact) =>
        artifact == null
            ? "{}"
            : JsonSerializer.Serialize(artifact, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static string SerializeSemanticContract(AgentToolSemanticSpec? semantic) =>
        semantic == null
            ? "{}"
            : JsonSerializer.Serialize(new
            {
                semantic.DisplayName,
                semantic.DomainSurface,
                semantic.OutputKind,
                semantic.InputArtifacts,
                semantic.OutputArtifacts,
                semantic.IdempotencyPolicy,
                semantic.RollbackPolicy,
                semantic.UserVisibleWhere,
                semantic.ResultSemantics
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

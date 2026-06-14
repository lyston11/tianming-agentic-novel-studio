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
        if (result.IsRepairable)
            return "repairable";
        if (!string.IsNullOrWhiteSpace(result.MissingPrerequisite))
            return "missing_prerequisite";
        return "tool_failure";
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

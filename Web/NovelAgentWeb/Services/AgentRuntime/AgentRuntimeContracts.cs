using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public static class AgentRuntimeRunStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string WaitingUser = "waiting_user";
    public const string Paused = "paused";

    public static readonly string[] Active = { Queued, Running };
}

public static class AgentRuntimeRunMode
{
    public const string Answer = "answer";
    public const string Inspect = "inspect";
    public const string Production = "production";
}

public static class AgentRuntimeEventSurface
{
    public const string Chat = "chat";
    public const string Workflow = "workflow";
    public const string Library = "library";
    public const string Knowledge = "knowledge";
    public const string AdminDebug = "admin_debug";
}

public static class AgentRuntimeEventDisplayPolicy
{
    public const string Hidden = "hidden";
    public const string Inline = "inline";
    public const string Collapsible = "collapsible";
    public const string Timeline = "timeline";
    public const string DebugOnly = "debug_only";
}

public static class AgentInterruptStatus
{
    public const string Pending = "pending";
    public const string Consumed = "consumed";
    public const string Rejected = "rejected";
}

public sealed record AgentRuntimeBudget(
    int MaxLoopIterations = 0,
    int MaxToolCalls = 0,
    int MaxModelCalls = 0,
    int MaxDurationSeconds = 0,
    int MaxRevisions = 0,
    int MaxRewrites = 0);

public sealed record AgentRuntimeRunCacheSnapshot(
    string RuntimeRunId,
    string UserId,
    string SessionId,
    string? ProjectId,
    string Status,
    string Mode,
    string CurrentPhase,
    string LastMessage,
    string ActiveTool,
    int CurrentStep,
    bool CancelRequested,
    DateTime UpdatedAt,
    DateTime HeartbeatAt);

public sealed record AgentRuntimeActiveRunState(
    AgentRuntimeRun Run,
    DateTime HeartbeatAt,
    bool FromDistributedCache);

public sealed record AgentRuntimeActiveSessionCursor(
    string RuntimeRunId,
    string UserId,
    string SessionId,
    string? ProjectId,
    DateTime UpdatedAt);

public sealed record AgentRuntimeInterruptCacheItem(
    string InterruptId,
    string RuntimeRunId,
    string UserId,
    string SessionId,
    string? ProjectId,
    string Kind,
    string Message,
    int Priority,
    DateTime CreatedAt);

public sealed record AgentRuntimeInterruptQueueSnapshot(
    string RuntimeRunId,
    IReadOnlyList<AgentRuntimeInterruptCacheItem> Pending,
    int PendingCount,
    DateTime UpdatedAt);

public sealed record AgentRuntimeFailure(
    string Code,
    string Stage,
    string Message,
    bool Recoverable = false,
    string RecommendedAction = "",
    IReadOnlyList<string>? ArtifactIds = null);

public sealed class AgentRuntimeRunCancelledException : OperationCanceledException
{
    public AgentRuntimeRunCancelledException(string runtimeRunId, string stage)
        : base(BuildMessage(runtimeRunId, stage))
    {
        RuntimeRunId = runtimeRunId;
        Stage = stage;
    }

    public string RuntimeRunId { get; }

    public string Stage { get; }

    private static string BuildMessage(string runtimeRunId, string stage)
    {
        var normalizedStage = string.IsNullOrWhiteSpace(stage) ? "runtime_cancelled" : stage.Trim();
        return $"Runtime run {runtimeRunId} was cancelled at {normalizedStage}.";
    }
}

public sealed record CreateAgentRuntimeRunRequest(
    string UserId,
    string SessionId,
    string? ProjectId,
    string UserMessage,
    string Mode = AgentRuntimeRunMode.Inspect,
    string? IdempotencyKey = null,
    string? SourceMessageId = null,
    AgentRuntimeBudget? Budget = null);

public sealed record CreateAgentInterruptRequest(
    string RuntimeRunId,
    string UserId,
    string SessionId,
    string? ProjectId,
    string Kind,
    string Message,
    int Priority);

public sealed record CreateAgentRuntimeEventRequest(
    string RuntimeRunId,
    string UserId,
    string SessionId,
    string? ProjectId,
    string Type,
    string Message,
    object? Data = null,
    string Stage = "",
    string Status = "",
    string? ArtifactType = null,
    string? ArtifactId = null,
    string DisplaySurface = AgentRuntimeEventSurface.Chat,
    string DisplayPolicy = AgentRuntimeEventDisplayPolicy.Collapsible,
    bool PublishToSse = false);

public interface IAgentRuntimeRunService
{
    Task<AgentRuntimeRun> CreateQueuedAsync(CreateAgentRuntimeRunRequest request, CancellationToken ct = default);
    Task<AgentRuntimeRun?> TryGetAsync(string runtimeRunId, CancellationToken ct = default);
    Task<AgentRuntimeRun?> TryGetActiveAsync(string userId, string sessionId, CancellationToken ct = default);
    Task<AgentRuntimeActiveRunState?> TryGetActiveStateAsync(string userId, string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentRuntimeActiveSessionCursor>> ListActiveSessionCursorsAsync(int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<AgentRuntimeRun>> ListQueuedAsync(int limit = 500, CancellationToken ct = default);
    Task<AgentRuntimeRun> MarkRunningAsync(string runtimeRunId, CancellationToken ct = default);
    Task<AgentRuntimeRun> UpdateProgressAsync(string runtimeRunId, string phase, string message, string? activeTool = null, int? currentStep = null, CancellationToken ct = default);
    Task<AgentRuntimeRun> MarkCompletedAsync(string runtimeRunId, object? result, CancellationToken ct = default);
    Task<AgentRuntimeRun> MarkFailedAsync(string runtimeRunId, string errorMessage, CancellationToken ct = default);
    Task<AgentRuntimeRun> MarkFailedAsync(string runtimeRunId, AgentRuntimeFailure failure, CancellationToken ct = default);
    Task<AgentRuntimeRun> MarkCancelledAsync(string runtimeRunId, string stage, string message, CancellationToken ct = default);
    Task<AgentRuntimeRun> RequestCancelAsync(string runtimeRunId, CancellationToken ct = default);
    Task<int> FailStaleActiveRunsAsync(TimeSpan staleAfter, string reason, CancellationToken ct = default);
}

public interface IAgentInterruptService
{
    Task<AgentInterrupt> AddAsync(CreateAgentInterruptRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AgentInterrupt>> GetPendingAsync(string runtimeRunId, CancellationToken ct = default);
    Task<AgentRuntimeInterruptQueueSnapshot?> TryGetPendingSnapshotAsync(string runtimeRunId, CancellationToken ct = default);
    Task<AgentInterrupt?> MarkConsumedAsync(string interruptId, object? decision, CancellationToken ct = default);
    Task<AgentInterrupt?> MarkRejectedAsync(string interruptId, string reason, CancellationToken ct = default);
}

public interface IAgentRuntimeEventService
{
    Task<AgentRuntimeEvent> AppendAsync(CreateAgentRuntimeEventRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AgentRuntimeEvent>> GetRecentAsync(
        string userId,
        string sessionId,
        int limit = 50,
        CancellationToken ct = default,
        string? afterEventId = null);
    Task<IReadOnlyList<AgentRuntimeEvent>> GetForRunAsync(
        string runtimeRunId,
        int limit = 100,
        CancellationToken ct = default,
        string? afterEventId = null);
}

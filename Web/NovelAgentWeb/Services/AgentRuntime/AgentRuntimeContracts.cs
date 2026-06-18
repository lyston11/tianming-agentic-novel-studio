using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public static class AgentRuntimeRunStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static readonly string[] Active = { Queued, Running };
}

public static class AgentInterruptStatus
{
    public const string Pending = "pending";
    public const string Consumed = "consumed";
    public const string Rejected = "rejected";
}

public sealed record CreateAgentRuntimeRunRequest(
    string UserId,
    string SessionId,
    string? ProjectId,
    string UserMessage);

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
    object? Data = null);

public interface IAgentRuntimeRunService
{
    Task<AgentRuntimeRun> CreateQueuedAsync(CreateAgentRuntimeRunRequest request, CancellationToken ct = default);
    Task<AgentRuntimeRun?> TryGetAsync(string runtimeRunId, CancellationToken ct = default);
    Task<AgentRuntimeRun?> TryGetActiveAsync(string userId, string sessionId, CancellationToken ct = default);
    Task<AgentRuntimeRun> MarkRunningAsync(string runtimeRunId, CancellationToken ct = default);
    Task<AgentRuntimeRun> UpdateProgressAsync(string runtimeRunId, string phase, string message, string? activeTool = null, int? currentStep = null, CancellationToken ct = default);
    Task<AgentRuntimeRun> MarkCompletedAsync(string runtimeRunId, object? result, CancellationToken ct = default);
    Task<AgentRuntimeRun> MarkFailedAsync(string runtimeRunId, string errorMessage, CancellationToken ct = default);
    Task<AgentRuntimeRun> RequestCancelAsync(string runtimeRunId, CancellationToken ct = default);
}

public interface IAgentInterruptService
{
    Task<AgentInterrupt> AddAsync(CreateAgentInterruptRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AgentInterrupt>> GetPendingAsync(string runtimeRunId, CancellationToken ct = default);
    Task<AgentInterrupt?> MarkConsumedAsync(string interruptId, object? decision, CancellationToken ct = default);
    Task<AgentInterrupt?> MarkRejectedAsync(string interruptId, string reason, CancellationToken ct = default);
}

public interface IAgentRuntimeEventService
{
    Task<AgentRuntimeEvent> AppendAsync(CreateAgentRuntimeEventRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AgentRuntimeEvent>> GetRecentAsync(string userId, string sessionId, int limit = 50, CancellationToken ct = default);
}

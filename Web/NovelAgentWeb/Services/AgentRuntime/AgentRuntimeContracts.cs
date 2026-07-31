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

    public static readonly string[] Active = [Queued, Running];
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

public sealed record AgentRuntimeActiveRunState(
    AgentRuntimeRun Run,
    DateTime HeartbeatAt,
    bool FromDistributedCache);

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

public interface ILegacyRuntimeAuditReader
{
    Task<AgentRuntimeRun?> TryGetAsync(string runtimeRunId, CancellationToken cancellationToken = default);
    Task<AgentRuntimeActiveRunState?> TryGetActiveStateAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);
}

public interface IAgentRuntimeEventService
{
    Task<AgentRuntimeEvent> AppendAsync(CreateAgentRuntimeEventRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentRuntimeEvent>> GetRecentAsync(
        string userId,
        string sessionId,
        int limit = 50,
        CancellationToken cancellationToken = default,
        string? afterEventId = null);
    Task<IReadOnlyList<AgentRuntimeEvent>> GetForRunAsync(
        string runtimeRunId,
        int limit = 100,
        CancellationToken cancellationToken = default,
        string? afterEventId = null);
}

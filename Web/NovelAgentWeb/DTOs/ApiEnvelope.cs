namespace TM.Web.NovelAgentWeb.DTOs;

public sealed class ApiEnvelope<T>
{
    public const string CurrentApiVersion = "v1";
    public const string CurrentToolSchemaVersion = "agent-tools-v1";
    public const string CurrentAgentLoopVersion = "agent-loop-v1";
    public const string CurrentKernelVersion = "agentic-tianming-v1";

    public bool Success { get; init; }
    public T? Data { get; init; }
    public ApiErrorDto? Error { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public DateTime ServerTime { get; init; } = DateTime.UtcNow;
    public string ApiVersion { get; init; } = CurrentApiVersion;
    public string ToolSchemaVersion { get; init; } = CurrentToolSchemaVersion;
    public string AgentLoopVersion { get; init; } = CurrentAgentLoopVersion;
    public string KernelVersion { get; init; } = CurrentKernelVersion;

    public static ApiEnvelope<T> Ok(T data, string requestId = "") =>
        new()
        {
            Success = true,
            Data = data,
            RequestId = requestId,
            ServerTime = DateTime.UtcNow,
            ApiVersion = CurrentApiVersion,
            ToolSchemaVersion = CurrentToolSchemaVersion,
            AgentLoopVersion = CurrentAgentLoopVersion,
            KernelVersion = CurrentKernelVersion
        };

    public static ApiEnvelope<T> Fail(ApiErrorDto error, string requestId = "") =>
        new()
        {
            Success = false,
            Error = error,
            RequestId = requestId,
            ServerTime = DateTime.UtcNow,
            ApiVersion = CurrentApiVersion,
            ToolSchemaVersion = CurrentToolSchemaVersion,
            AgentLoopVersion = CurrentAgentLoopVersion,
            KernelVersion = CurrentKernelVersion
        };
}

public sealed class ApiErrorDto
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public bool Recoverable { get; init; }
    public string RecommendedAction { get; init; } = string.Empty;
    public bool RequiresUserDecision { get; init; }
    public IReadOnlyList<string> ArtifactIds { get; init; } = Array.Empty<string>();
}

public static class ApiErrors
{
    public static ApiErrorDto BadRequest(
        string message,
        string code = "BAD_REQUEST",
        string stage = "request_validation",
        string recommendedAction = "请检查请求参数后重试。",
        IReadOnlyList<string>? artifactIds = null,
        bool requiresUserDecision = false) =>
        Create(code, message, stage, recoverable: true, recommendedAction, artifactIds, requiresUserDecision);

    public static ApiErrorDto NotFound(
        string message,
        string code = "RESOURCE_NOT_FOUND",
        string stage = "resource_lookup",
        string recommendedAction = "请确认资源 ID 是否正确。",
        IReadOnlyList<string>? artifactIds = null,
        bool requiresUserDecision = false) =>
        Create(code, message, stage, recoverable: false, recommendedAction, artifactIds, requiresUserDecision);

    public static ApiErrorDto Internal(
        string message,
        string code = "INTERNAL_ERROR",
        string stage = "server_execution",
        string recommendedAction = "请稍后重试或查看服务日志。",
        IReadOnlyList<string>? artifactIds = null,
        bool requiresUserDecision = false) =>
        Create(code, message, stage, recoverable: false, recommendedAction, artifactIds, requiresUserDecision);

    public static ApiErrorDto ServiceUnavailable(
        string message,
        string code = "SERVICE_UNAVAILABLE",
        string stage = "service_health",
        string recommendedAction = "请稍后重试或查看运行状态。",
        IReadOnlyList<string>? artifactIds = null,
        bool requiresUserDecision = false) =>
        Create(code, message, stage, recoverable: true, recommendedAction, artifactIds, requiresUserDecision);

    public static ApiErrorDto Create(
        string code,
        string message,
        string stage,
        bool recoverable,
        string recommendedAction,
        IReadOnlyList<string>? artifactIds = null,
        bool requiresUserDecision = false) =>
        new()
        {
            Code = code,
            Message = message,
            Stage = stage,
            Recoverable = recoverable,
            RecommendedAction = recommendedAction,
            RequiresUserDecision = requiresUserDecision,
            ArtifactIds = artifactIds ?? Array.Empty<string>()
        };
}

using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public sealed record AgentToolExecutionStart(
    string UserId,
    string? ProjectId,
    string SessionId,
    string? RunId,
    string Phase,
    string Risk,
    AgentToolCall Call,
    AgentToolSideEffectSpec? SideEffects = null);

public sealed class AgentToolExecutionSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string ResultPhase { get; set; } = string.Empty;
    public string ResultMessage { get; set; } = string.Empty;
    public string RecommendedNextTool { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public interface IAgentToolExecutionLedger
{
    Task<AgentToolExecution> StartAsync(AgentToolExecutionStart start, CancellationToken ct = default);
    Task RebindProjectAsync(string executionId, string projectId, CancellationToken ct = default);
    Task CompleteAsync(string executionId, AgentToolExecutionResult result, CancellationToken ct = default);
    Task<IReadOnlyList<AgentToolExecutionSnapshot>> GetRecentAsync(
        string userId,
        string sessionId,
        string? projectId,
        CancellationToken ct = default);
}

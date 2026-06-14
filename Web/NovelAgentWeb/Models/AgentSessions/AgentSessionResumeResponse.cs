using TM.Web.NovelAgentWeb.Support;
using TM.Web.NovelAgentWeb.Services.AgentTools;

namespace TM.Web.NovelAgentWeb.Models.AgentSessions;

public sealed class AgentSessionResumeResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Title { get; set; } = "新会话";
    public string Phase { get; set; } = "idle";
    public string ActiveProjectId { get; set; } = string.Empty;
    public string? ActiveRunId { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public IReadOnlyList<AgentConversationTurn> Messages { get; set; } = Array.Empty<AgentConversationTurn>();
    public AgentWorkingMemorySnapshot Memory { get; set; } = new();
    public AgentMissionPlan MissionPlan { get; set; } = new();
    public AgentToolCall? PendingToolCall { get; set; }
    public AgentPendingConfirmation? PendingConfirmation { get; set; }
    public bool HasPendingTool { get; set; }
    public bool HasPendingConfirmation { get; set; }
    public string? DiscoveredPhase { get; set; }
    public IReadOnlyList<ToolSchema> DiscoveredTools { get; set; } = Array.Empty<ToolSchema>();
    public string? ToolSearchCacheVersion { get; set; }
    public DateTime? LastToolSearchAt { get; set; }
    public bool ToolSearchCacheFresh { get; set; }
    public string ToolSearchCacheSource { get; set; } = "none";
    public IReadOnlyList<AgentToolExecutionSnapshot> RecentToolExecutions { get; set; } = Array.Empty<AgentToolExecutionSnapshot>();
    public IReadOnlyList<string> RunHistory { get; set; } = Array.Empty<string>();
}

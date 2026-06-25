using TM.Web.NovelAgentWeb.Support;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using System.Text.Json;

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
    public IReadOnlyList<AgentRuntimeEventView> RecentRuntimeEvents { get; set; } = Array.Empty<AgentRuntimeEventView>();
    public IReadOnlyList<string> RunHistory { get; set; } = Array.Empty<string>();
}

public sealed class AgentRuntimeEventView
{
    public string EventId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public string SourceMessageId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string DisplaySurface { get; set; } = string.Empty;
    public string DisplayPolicy { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public JsonElement Data { get; set; }
    public DateTime Timestamp { get; set; }
}

using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Models.AgentSessions;

/// <summary>
/// Agent session response with metadata and state.
/// </summary>
public class AgentSessionResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Title { get; set; } = "新会话";
    public string Phase { get; set; } = "idle";
    public string ActiveProjectId { get; set; } = string.Empty;
    public string? ActiveRunId { get; set; }
    public bool IsArchived { get; set; }
    public IReadOnlyList<string> RunHistory { get; set; } = Array.Empty<string>();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public IReadOnlyList<AgentConversationTurn> Messages { get; set; } = Array.Empty<AgentConversationTurn>();
    public AgentWorkingMemorySnapshot Memory { get; set; } = new();
    public int MessageCount { get; set; }
}

/// <summary>
/// Request to update session metadata.
/// </summary>
public class UpdateAgentSessionRequest
{
    public string? Title { get; set; }
    public bool? IsArchived { get; set; }
}

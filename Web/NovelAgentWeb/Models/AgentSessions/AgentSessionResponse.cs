namespace TM.Web.NovelAgentWeb.Models.AgentSessions;

/// <summary>
/// Agent session response with metadata and state.
/// </summary>
public class AgentSessionResponse
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string Title { get; set; } = "新会话";
    public string Phase { get; set; } = "idle";
    public bool IsArchived { get; set; }
    public string SessionData { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Summary response for session list (without full session data).
/// </summary>
public class AgentSessionSummary
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = "新会话";
    public string Phase { get; set; } = "idle";
    public string? ProjectId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Request to update session metadata.
/// </summary>
public class UpdateAgentSessionRequest
{
    public string? Title { get; set; }
    public bool? IsArchived { get; set; }
}

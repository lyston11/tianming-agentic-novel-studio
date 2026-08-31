using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Data.Entities;

/// <summary>
/// Represents an agent conversation session with working memory and state.
/// </summary>
public class AgentSession
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public long BindingVersion { get; set; }
    public string? IdempotencyKey { get; set; }
    public string Title { get; set; } = "新会话";
    public bool IsArchived { get; set; } = false;
    public string? SessionData { get; set; }  // Lightweight runtime metadata; chat turns live in agent_chat_turns.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public LayeredChatHistory? LayeredHistory { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
    public ICollection<AgentChatTurn> ChatTurns { get; set; } = new List<AgentChatTurn>();
    public ICollection<AgentChatSummary> ChatSummaries { get; set; } = new List<AgentChatSummary>();
    public ICollection<ProjectContextActivation> ProjectContextActivations { get; set; } = new List<ProjectContextActivation>();
}

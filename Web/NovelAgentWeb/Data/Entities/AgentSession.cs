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
    public string Title { get; set; } = "新会话";
    public bool IsArchived { get; set; } = false;
    public string? SessionData { get; set; }  // JSON containing chat history, working memory, run history
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public LayeredChatHistory? LayeredHistory { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
    public ICollection<AgentChatTurn> ChatTurns { get; set; } = new List<AgentChatTurn>();
    public ICollection<AgentChatSummary> ChatSummaries { get; set; } = new List<AgentChatSummary>();
}

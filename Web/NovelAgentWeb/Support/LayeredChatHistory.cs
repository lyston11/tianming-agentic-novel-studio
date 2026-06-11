namespace TM.Web.NovelAgentWeb.Support;

public class LayeredChatHistory
{
    public string? MetaSummary { get; set; }
    public List<ChatSummary> Summaries { get; set; } = new();
    public List<ChatMessage> RecentMessages { get; set; } = new();
}

public class ChatSummary
{
    public int StartTurn { get; set; }
    public int EndTurn { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<string> KeyDecisions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

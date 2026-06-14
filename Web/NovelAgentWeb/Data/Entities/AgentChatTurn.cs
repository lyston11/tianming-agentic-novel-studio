namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentChatTurn
{
    public string Id { get; set; } = null!;
    public string SessionId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public int TurnIndex { get; set; }
    public string Role { get; set; } = null!;
    public string Content { get; set; } = null!;
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CompressedIntoSummaryId { get; set; }
}

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentChatSummary
{
    public string Id { get; set; } = null!;
    public string SessionId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public int StartTurn { get; set; }
    public int EndTurn { get; set; }
    public string SummaryType { get; set; } = "summary";
    public string Content { get; set; } = null!;
    public string? KeyDecisionsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

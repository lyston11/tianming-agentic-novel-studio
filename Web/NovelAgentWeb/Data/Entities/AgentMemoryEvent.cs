namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentMemoryEvent
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string? SessionId { get; set; }
    public string? RunId { get; set; }
    public string SourceType { get; set; } = null!;
    public string TriggerType { get; set; } = null!;
    public string MemoryScope { get; set; } = null!;
    public string MemoryKey { get; set; } = null!;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

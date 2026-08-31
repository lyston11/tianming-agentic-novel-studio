namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentMemoryVersion
{
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string? SessionId { get; set; }
    public string Scope { get; set; } = null!;
    public long Version { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

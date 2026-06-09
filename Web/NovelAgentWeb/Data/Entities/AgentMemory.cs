namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentMemory
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string MemoryType { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
}

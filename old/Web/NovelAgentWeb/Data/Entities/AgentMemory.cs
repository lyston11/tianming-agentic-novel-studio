namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentMemory
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string? SessionId { get; set; }
    public string MemoryType { get; set; } = null!;
    public string MemoryKey { get; set; } = string.Empty;
    public string Content { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
}

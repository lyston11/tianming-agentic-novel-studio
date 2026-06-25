namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentMemoryRead
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string? SessionId { get; set; }
    public string? RunId { get; set; }
    public string MemoryScope { get; set; } = null!;
    public string MemoryKeysJson { get; set; } = "[]";
    public string SourceType { get; set; } = "memory_repository";
    public string Consumer { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

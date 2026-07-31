namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class AuthorMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string MemoryKind { get; set; } = string.Empty;
    public string ContentJson { get; set; } = "{}";
    public string Source { get; set; } = "user";
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

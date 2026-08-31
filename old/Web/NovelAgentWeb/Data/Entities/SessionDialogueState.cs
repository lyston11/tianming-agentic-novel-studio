namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class SessionDialogueState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string MemoryKind { get; set; } = string.Empty;
    public string ContentJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

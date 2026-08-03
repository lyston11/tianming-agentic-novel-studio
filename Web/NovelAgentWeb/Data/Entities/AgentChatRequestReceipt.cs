namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class AgentChatRequestReceipt
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string RequestedSessionId { get; set; } = null!;
    public string CanonicalKey { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public string Status { get; set; } = "processing";
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public string? ResponseJson { get; set; }
    public string? ResolvedSessionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

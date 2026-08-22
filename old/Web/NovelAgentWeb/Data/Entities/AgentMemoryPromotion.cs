namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentMemoryPromotion
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string? SessionId { get; set; }
    public string? RunId { get; set; }
    public string SourceScope { get; set; } = null!;
    public string TargetScope { get; set; } = null!;
    public string SourceMemoryKey { get; set; } = null!;
    public string TargetMemoryKey { get; set; } = null!;
    public string PromotionReason { get; set; } = null!;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class ProductionBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string BookProductionId { get; set; } = string.Empty;
    public int BatchNumber { get; set; }
    public int StartChapterNumber { get; set; }
    public int EndChapterNumber { get; set; }
    public string Status { get; set; } = "planned";
    public string? TaskGraphVersionId { get; set; }
    public string? CanonBranchId { get; set; }
    public string AcceptanceActor { get; set; } = "human";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

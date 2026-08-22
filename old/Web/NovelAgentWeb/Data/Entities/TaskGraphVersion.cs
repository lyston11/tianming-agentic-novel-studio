namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class TaskGraphVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string? GoalRevisionId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = "compiled";
    public string GraphJson { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class OutboxEvent
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string? RuntimeRunId { get; set; }
    public string EventType { get; set; } = null!;
    public string AggregateType { get; set; } = null!;
    public string AggregateId { get; set; } = null!;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

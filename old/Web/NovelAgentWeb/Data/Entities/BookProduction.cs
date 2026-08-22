namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class BookProduction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string ExecutionStrategy { get; set; } = "interactive_batch";
    public string Status { get; set; } = "running";
    public int TargetStartChapterNumber { get; set; }
    public int TargetEndChapterNumber { get; set; }
    public int NextChapterNumber { get; set; }
    public int BatchSize { get; set; } = 5;
    public int CurrentBatchNumber { get; set; } = 1;
    public string CompletionCriteriaJson { get; set; } = "{}";
    public string PausePolicyJson { get; set; } = "{}";
    public long AggregateVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

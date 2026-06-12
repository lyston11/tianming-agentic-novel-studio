namespace TM.Web.NovelAgentWeb.Data.Entities;

public class KnowledgeProcessingTask
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Status { get; set; } = "pending";
    public string Strategy { get; set; } = "single_pass";
    public int Progress { get; set; }
    public int? TotalChunks { get; set; }
    public int ProcessedChunks { get; set; }
    public int ExtractedEntriesCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
}

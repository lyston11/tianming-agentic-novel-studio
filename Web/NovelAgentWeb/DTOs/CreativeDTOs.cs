namespace TM.Web.NovelAgentWeb.DTOs;

public sealed class CreateCreativeIntentApiRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string RawContent { get; set; } = string.Empty;
    public string NormalizedIntent { get; set; } = string.Empty;
    public string Source { get; set; } = "chat";
    public string TargetScope { get; set; } = "project";
    public string TargetVolumeId { get; set; } = string.Empty;
    public string TargetChapterId { get; set; } = string.Empty;
    public string TargetCharacterName { get; set; } = string.Empty;
    public string ImpactLevel { get; set; } = "future_carry";
    public bool RequiresConfirmation { get; set; }
    public string ConflictStatus { get; set; } = "unknown";
    public string MetadataJson { get; set; } = "{}";
}

public sealed class DecideCreativeIntentApiRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string Status { get; set; } = "candidate";
    public string DecisionReason { get; set; } = string.Empty;
    public string ConflictStatus { get; set; } = string.Empty;
    public bool MarkExecuted { get; set; }
}

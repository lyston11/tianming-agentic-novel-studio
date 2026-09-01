namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class GoalContextSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string CanonVersion { get; set; } = string.Empty;
    public string KnowledgeVersion { get; set; } = string.Empty;
    public string QualityContractVersion { get; set; } = string.Empty;
    public string StyleProfileVersion { get; set; } = string.Empty;
    public string ModelConfigVersionsJson { get; set; } = "{}";
    public string ProtocolVersionsJson { get; set; } = "{}";
    public string ContentHashesJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

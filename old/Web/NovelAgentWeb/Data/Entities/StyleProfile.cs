namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class StyleProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string DocumentBlobId { get; set; } = string.Empty;
    public long KnowledgeVersion { get; set; }
    public int Version { get; set; } = 1;
    public string ProfileKind { get; set; } = "abstract_features_only";
    public string FeaturesJson { get; set; } = "{}";
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

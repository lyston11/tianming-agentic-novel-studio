namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class ModelKernelConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string KernelName { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string Provider { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string CredentialReference { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public float Temperature { get; set; } = 0.7f;
    public int MaxOutputTokens { get; set; } = 4096;
    public int TimeoutSeconds { get; set; } = 120;
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
    public string FallbackJson { get; set; } = "[]";
    public string CustomInstructions { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

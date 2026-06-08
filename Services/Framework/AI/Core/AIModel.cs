namespace TM.Services.Framework.AI.Core;

public class AIModel
{
    [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("ProviderId")] public string ProviderId { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("DisplayName")] public string DisplayName { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("ContextWindow")] public int ContextWindow { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("MaxOutputTokens")] public int MaxOutputTokens { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SupportsFunctions")] public bool SupportsFunctions { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SupportsVision")] public bool SupportsVision { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SupportsArrayContent")] public bool SupportsArrayContent { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SupportsDeveloperMessage")] public bool SupportsDeveloperMessage { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SupportsStreamOptions")] public bool SupportsStreamOptions { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SupportsServiceTier")] public bool SupportsServiceTier { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SupportsEnableThinking")] public bool SupportsEnableThinking { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("ServiceTier")] public string? ServiceTier { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("CostPer1kInput")] public double CostPer1kInput { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("CostPer1kOutput")] public double CostPer1kOutput { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Order")] public int Order { get; set; }
}

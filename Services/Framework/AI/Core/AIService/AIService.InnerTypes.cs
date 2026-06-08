using System.Collections.Generic;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.Core;

public sealed partial class AIService : IAIConfigurationService, IAILibraryService, IAITextGenerationService
{
    #region 内部数据包装类

    private class CategoryWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("Categories")] public List<AICategory> Categories { get; set; } = new();
    }

    private class ProviderWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("Providers")] public List<AIProvider> Providers { get; set; } = new();
    }

    private class ModelWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("Models")] public List<AIModel> Models { get; set; } = new();
    }

    private class ConfigurationWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("Configurations")] public List<UserConfiguration> Configurations { get; set; } = new();
    }

    private class ModelCapabilityRuleSet
    {
        [System.Text.Json.Serialization.JsonPropertyName("Rules")] public List<ModelCapabilityRule> Rules { get; set; } = new();
    }

    private class ModelCapabilityRule
    {
        [System.Text.Json.Serialization.JsonPropertyName("ProviderId")] public string? ProviderId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ProviderNameContains")] public string? ProviderNameContains { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EndpointContains")] public string? EndpointContains { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ModelId")] public string? ModelId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ModelNameContains")] public string? ModelNameContains { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsArrayContent")] public bool? SupportsArrayContent { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsDeveloperMessage")] public bool? SupportsDeveloperMessage { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsStreamOptions")] public bool? SupportsStreamOptions { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsServiceTier")] public bool? SupportsServiceTier { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsEnableThinking")] public bool? SupportsEnableThinking { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ServiceTier")] public string? ServiceTier { get; set; }
    }

    #endregion
}

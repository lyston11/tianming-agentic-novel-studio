namespace TM.Services.Framework.AI.Core;

public class AIProvider
{
    [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("ApiEndpoint")] public string ApiEndpoint { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("RequiresApiKey")] public bool RequiresApiKey { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SupportsStreaming")] public bool SupportsStreaming { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Icon")] public string Icon { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Order")] public int Order { get; set; }
}

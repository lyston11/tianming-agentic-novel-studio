namespace TM.Services.Framework.AI.Core;

public class AICategory
{
    [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Icon")] public string Icon { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Order")] public int Order { get; set; }
}

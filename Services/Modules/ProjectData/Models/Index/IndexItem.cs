using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Index
{
    public class IndexItem
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;

        [JsonPropertyName("Type")] public string Type { get; set; } = string.Empty;

        [JsonPropertyName("BriefSummary")] public string BriefSummary { get; set; } = string.Empty;

        [JsonPropertyName("DeepSummary")] public string DeepSummary { get; set; } = string.Empty;

        [JsonPropertyName("RelationStrength")] public string RelationStrength { get; set; } = string.Empty;
    }
}

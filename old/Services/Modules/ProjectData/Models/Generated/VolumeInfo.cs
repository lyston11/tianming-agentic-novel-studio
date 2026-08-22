using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Generated
{
    public class VolumeInfo
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;

        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;

        [JsonPropertyName("Icon")] public string Icon { get; set; } = "Icon.Folder";

        [JsonPropertyName("Number")] public int Number { get; set; }

        [JsonPropertyName("Order")] public int Order { get; set; }

        [JsonPropertyName("Source")] public string Source { get; set; } = "volume_design";

        [JsonPropertyName("IsReadOnly")] public bool IsReadOnly { get; set; } = true;

    }
}

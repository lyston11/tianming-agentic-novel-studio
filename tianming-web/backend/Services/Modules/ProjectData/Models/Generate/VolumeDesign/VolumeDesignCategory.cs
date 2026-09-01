using System.Text.Json.Serialization;
using TM.Framework.Common.Models;

namespace TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign
{
    public class VolumeDesignCategory : ICategory
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Icon")] public string Icon { get; set; } = "Icon.Books";
        [JsonPropertyName("Order")] public int Order { get; set; } = 0;
        [JsonPropertyName("IsBuiltIn")] public bool IsBuiltIn { get; set; } = false;
        [JsonPropertyName("Level")] public int Level { get; set; } = 1;
        [JsonPropertyName("ParentCategory")] public string? ParentCategory { get; set; }
        [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
    }
}

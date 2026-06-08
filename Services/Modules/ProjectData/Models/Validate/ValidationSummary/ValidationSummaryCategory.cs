using System.Text.Json.Serialization;
using TM.Framework.Common.Models;

namespace TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary
{
    public class ValidationSummaryCategory : ICategory
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;

        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;

        [JsonPropertyName("Icon")] public string Icon { get; set; } = "Icon.Books";

        [JsonPropertyName("ParentCategory")] public string? ParentCategory { get; set; } = null;

        [JsonPropertyName("Level")] public int Level { get; set; } = 1;

        [JsonPropertyName("Order")] public int Order { get; set; }

        [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("IsBuiltIn")] public bool IsBuiltIn { get; set; } = false;
    }
}

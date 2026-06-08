using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Navigation
{
    public class ModuleInfo
    {
        [JsonPropertyName("ModuleType")]
        public string ModuleType { get; set; } = string.Empty;

        [JsonPropertyName("SubModule")]
        public string SubModule { get; set; } = string.Empty;

        [JsonPropertyName("SubModuleDisplayName")]
        public string SubModuleDisplayName { get; set; } = string.Empty;

        [JsonPropertyName("FunctionName")]
        public string FunctionName { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("Icon")]
        public string Icon { get; set; } = string.Empty;

        [JsonPropertyName("StoragePath")]
        public string StoragePath { get; set; } = string.Empty;

        [JsonPropertyName("ViewPath")]
        public string ViewPath { get; set; } = string.Empty;
    }
}

using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary
{
    public class ModuleValidationResult
    {
        [JsonPropertyName("ModuleName")] public string ModuleName { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")] public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("VerificationType")] public string VerificationType { get; set; } = string.Empty;

        [JsonPropertyName("Result")] public string Result { get; set; } = "未校验";

        public string ResultIcon => Result switch
        {
            "通过" => "Icon.CheckCircle",
            "警告" => "Icon.Warning",
            "失败" => "Icon.Error",
            "未校验" => "Icon.Warning",
            _ => "Icon.Clock"
        };

        [JsonPropertyName("IssueDescription")] public string IssueDescription { get; set; } = string.Empty;
        [JsonPropertyName("FixSuggestion")] public string FixSuggestion { get; set; } = string.Empty;
        [JsonPropertyName("ExtendedDataJson")] public string ExtendedDataJson { get; set; } = string.Empty;
        [JsonPropertyName("ProblemItemsJson")] public string ProblemItemsJson { get; set; } = string.Empty;
    }
}

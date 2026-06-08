using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary
{
    public class ProblemItem
    {
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("Details")] public string? Details { get; set; }
        [JsonPropertyName("Suggestion")] public string? Suggestion { get; set; }
        [JsonPropertyName("ChapterId")] public string? ChapterId { get; set; }
        [JsonPropertyName("ChapterTitle")] public string? ChapterTitle { get; set; }
    }
}

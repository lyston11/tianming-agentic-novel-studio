using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class ContentGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "ContentGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Chapters")] public Dictionary<string, ContentGuideEntry> Chapters { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ChapterSummaries")] public Dictionary<string, string> ChapterSummaries { get; set; } = new();
    }
}

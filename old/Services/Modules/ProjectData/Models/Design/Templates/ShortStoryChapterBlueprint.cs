using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Design.Templates
{
    public class ShortStoryChapterBlueprint
    {
        [JsonPropertyName("ChapterIndex")] public int ChapterIndex { get; set; }
        [JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("KeyEvents")] public string KeyEvents { get; set; } = string.Empty;
        [JsonPropertyName("Characters")] public string Characters { get; set; } = string.Empty;
        [JsonPropertyName("EndingNote")] public string EndingNote { get; set; } = string.Empty;
        [JsonPropertyName("TargetWordCount")] public string TargetWordCount { get; set; } = string.Empty;
    }
}

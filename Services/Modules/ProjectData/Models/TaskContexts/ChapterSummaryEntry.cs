using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.TaskContexts
{
    public class ChapterSummaryEntry
    {
        [JsonPropertyName("ChapterId")]
        public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("Title")]
        public string Title { get; set; } = string.Empty;
        [JsonPropertyName("Summary")]
        public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("ChapterNumber")]
        public int ChapterNumber { get; set; }
    }
}

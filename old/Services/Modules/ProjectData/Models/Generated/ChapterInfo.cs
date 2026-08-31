using System;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Generated
{
    public class ChapterInfo
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("VolumeNumber")] public int VolumeNumber { get; set; }
        [JsonPropertyName("ChapterNumber")] public int ChapterNumber { get; set; }
        [JsonPropertyName("WordCount")] public int WordCount { get; set; }
        [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; }
        [JsonPropertyName("ModifiedTime")] public DateTime ModifiedTime { get; set; }
        [JsonPropertyName("FilePath")] public string FilePath { get; set; } = string.Empty;
    }
}

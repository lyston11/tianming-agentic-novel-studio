using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class TimelineGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "TimelineGuide";
        [System.Text.Json.Serialization.JsonPropertyName("ChapterTimeline")] public List<ChapterTimeEntry> ChapterTimeline { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("CharacterLocations")] public Dictionary<string, CharacterLocationEntry> CharacterLocations { get; set; } = new();
    }

    public class ChapterTimeEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("TimePeriod")] public string TimePeriod { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ElapsedTime")] public string ElapsedTime { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("KeyTimeEvent")] public string KeyTimeEvent { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class CharacterLocationEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("CharacterName")] public string CharacterName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CurrentLocation")] public string CurrentLocation { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("LastUpdatedChapter")] public string LastUpdatedChapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("MovementHistory")] public List<MovementRecord> MovementHistory { get; set; } = new();
    }

    public class MovementRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("FromLocation")] public string FromLocation { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ToLocation")] public string ToLocation { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }
}

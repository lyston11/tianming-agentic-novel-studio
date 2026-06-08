using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class BlueprintGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "BlueprintGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Chapters")] public Dictionary<string, ChapterGuideEntry> Chapters { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ReverseIndex")] public ReverseIndex ReverseIndex { get; set; } = new();
    }

    public class ChapterGuideEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ChapterGoal")] public string ChapterGoal { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ContextIds")] public ContextIdCollection ContextIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Rhythm")] public RhythmInfo Rhythm { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Scenes")] public List<SceneGuideEntry> Scenes { get; set; } = new();
    }

    public class ReverseIndex
    {
        [System.Text.Json.Serialization.JsonPropertyName("ByCharacter")] public Dictionary<string, List<string>> ByCharacter { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ByLocation")] public Dictionary<string, List<string>> ByLocation { get; set; } = new();
    }
}

using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class ContentGuideEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ContextIds")] public ContextIdCollection ContextIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Rhythm")] public RhythmInfo Rhythm { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Scenes")] public List<SceneGuideEntry> Scenes { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ChapterNumber")] public int ChapterNumber { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Volume")] public string Volume { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ChapterTheme")] public string ChapterTheme { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("MainGoal")] public string MainGoal { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("KeyTurn")] public string KeyTurn { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Hook")] public string Hook { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("WorldInfoDrop")] public string WorldInfoDrop { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CharacterArcProgress")] public string CharacterArcProgress { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Foreshadowing")] public string Foreshadowing { get; set; } = string.Empty;
    }
}

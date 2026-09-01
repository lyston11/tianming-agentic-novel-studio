namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class RhythmInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("PaceType")] public string PaceType { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Intensity")] public string Intensity { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("EmotionalTone")] public string EmotionalTone { get; set; } = string.Empty;
    }
}

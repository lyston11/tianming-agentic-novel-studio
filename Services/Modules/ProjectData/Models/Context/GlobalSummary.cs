using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Services.Modules.ProjectData.Models.Index;

namespace TM.Services.Modules.ProjectData.Models.Context
{
    public class GlobalSummary
    {

        [JsonPropertyName("StorySummary")] public string StorySummary { get; set; } = string.Empty;
        [JsonPropertyName("CoreRules")] public List<IndexItem> CoreRules { get; set; } = new();
        [JsonPropertyName("CoreFactions")] public List<IndexItem> CoreFactions { get; set; } = new();
        [JsonPropertyName("MainConflict")] public string MainConflict { get; set; } = string.Empty;
        [JsonPropertyName("Progress")] public ProgressInfo Progress { get; set; } = new();
        [JsonPropertyName("UsedElements")] public UsedElementsIndex UsedElements { get; set; } = new();
        [JsonPropertyName("CompletedLayers")] public List<string> CompletedLayers { get; set; } = new();
    }

    public class ProgressInfo
    {
        [JsonPropertyName("TotalChapters")] public int TotalChapters { get; set; }
        [JsonPropertyName("CompletedChapters")] public int CompletedChapters { get; set; }
        [JsonPropertyName("CurrentPhase")] public string CurrentPhase { get; set; } = string.Empty;

        public string CompletionRate => TotalChapters > 0
            ? $"{CompletedChapters * 100 / TotalChapters}%"
            : "0%";
    }

    public class UsedElementsIndex
    {
        [JsonPropertyName("UsedAbilities")] public List<string> UsedAbilities { get; set; } = new();
        [JsonPropertyName("UsedPlotPatterns")] public List<string> UsedPlotPatterns { get; set; } = new();
        [JsonPropertyName("PlantedForeshadowings")] public List<string> PlantedForeshadowings { get; set; } = new();
        [JsonPropertyName("ResolvedForeshadowings")] public List<string> ResolvedForeshadowings { get; set; } = new();
    }
}

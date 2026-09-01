using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Modules.ProjectData.Models.Context
{
    public class TrackingStatus
    {
        [JsonPropertyName("CharacterStates")] public List<CharacterState> CharacterStates { get; set; } = new();
        [JsonPropertyName("ConflictProgress")] public List<ConflictProgress> ConflictProgress { get; set; } = new();
        [JsonPropertyName("ForeshadowingStats")] public ForeshadowingStats ForeshadowingStats { get; set; } = new();
        [JsonPropertyName("PlotPoints")] public PlotPointsIndex PlotPoints { get; set; } = new();
    }

    public class CharacterState
    {
        [JsonPropertyName("CharacterId")] public string CharacterId { get; set; } = string.Empty;
        [JsonPropertyName("CharacterName")] public string CharacterName { get; set; } = string.Empty;

        [JsonPropertyName("CurrentStatus")] public string CurrentStatus { get; set; } = string.Empty;

        [JsonPropertyName("LastAppearanceChapter")] public string LastAppearanceChapter { get; set; } = string.Empty;

        [JsonPropertyName("CurrentGoal")] public string CurrentGoal { get; set; } = string.Empty;
    }

    public class ConflictProgress
    {
        [JsonPropertyName("ConflictId")] public string ConflictId { get; set; } = string.Empty;
        [JsonPropertyName("ConflictName")] public string ConflictName { get; set; } = string.Empty;
        [JsonPropertyName("ProgressPercent")] public int ProgressPercent { get; set; }
        [JsonPropertyName("CurrentPhase")] public string CurrentPhase { get; set; } = string.Empty;
        [JsonPropertyName("NextExpectedEvent")] public string NextExpectedEvent { get; set; } = string.Empty;
    }

    public class ForeshadowingStats
    {
        [JsonPropertyName("Total")] public int Total { get; set; }
        [JsonPropertyName("Planted")] public int Planted { get; set; }
        [JsonPropertyName("Resolved")] public int Resolved { get; set; }
        [JsonPropertyName("PendingResolution")] public List<string> PendingResolution { get; set; } = new();
    }
}

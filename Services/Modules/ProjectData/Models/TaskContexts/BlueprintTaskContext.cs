using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.TaskContexts
{
    public class BlueprintTaskContext
    {
        [JsonPropertyName("ChapterId")]
        public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("Title")]
        public string Title { get; set; } = string.Empty;
        [JsonPropertyName("ChapterGoal")]
        public string ChapterGoal { get; set; } = string.Empty;
        [JsonPropertyName("Characters")]
        public List<CharacterRulesData> Characters { get; set; } = new();
        [JsonPropertyName("Locations")]
        public List<LocationRulesData> Locations { get; set; } = new();
        [JsonPropertyName("PlotRules")]
        public List<PlotRulesData> PlotRules { get; set; } = new();
        [JsonPropertyName("Factions")]
        public List<FactionRulesData> Factions { get; set; } = new();
        [JsonPropertyName("VolumeOutline")]
        public OutlineData VolumeOutline { get; set; } = new();
        [JsonPropertyName("PreviousChapterSummary")]
        public string PreviousChapterSummary { get; set; } = string.Empty;
        [JsonPropertyName("Rhythm")]
        public RhythmInfo Rhythm { get; set; } = new();
    }
}

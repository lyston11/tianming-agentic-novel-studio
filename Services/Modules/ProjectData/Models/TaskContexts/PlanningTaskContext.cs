using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.TaskContexts
{
    public class PlanningTaskContext
    {
        [JsonPropertyName("VolumeId")]
        public string VolumeId { get; set; } = string.Empty;
        [JsonPropertyName("VolumeOutline")]
        public OutlineData VolumeOutline { get; set; } = new();
        [JsonPropertyName("Characters")]
        public List<CharacterRulesData> Characters { get; set; } = new();
        [JsonPropertyName("PlotRules")]
        public List<PlotRulesData> PlotRules { get; set; } = new();
        public Dictionary<string, PlanningChapterEntry> ChapterPlans { get; set; } = new();
    }
}

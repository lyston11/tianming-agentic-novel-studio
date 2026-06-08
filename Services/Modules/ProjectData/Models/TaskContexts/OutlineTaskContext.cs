using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.TaskContexts
{
    public class OutlineTaskContext
    {
        [JsonPropertyName("VolumeId")]
        public string VolumeId { get; set; } = string.Empty;
        [JsonPropertyName("VolumeNumber")]
        public int VolumeNumber { get; set; }
        [JsonPropertyName("Theme")]
        public string Theme { get; set; } = string.Empty;
        [JsonPropertyName("Characters")]
        public List<CharacterRulesData> Characters { get; set; } = new();
        [JsonPropertyName("Locations")]
        public List<LocationRulesData> Locations { get; set; } = new();
        [JsonPropertyName("PlotRules")]
        public List<PlotRulesData> PlotRules { get; set; } = new();
        [JsonPropertyName("Factions")]
        public List<FactionRulesData> Factions { get; set; } = new();
        [JsonPropertyName("PreviousOutlines")]
        public List<OutlineData> PreviousOutlines { get; set; } = new();
    }
}

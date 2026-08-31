using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Design
{
    public class PlotContext
    {
        [JsonPropertyName("Templates")]
        public TemplatesContext Templates { get; set; } = new();

        [JsonPropertyName("Worldview")]
        public WorldviewContext Worldview { get; set; } = new();

        [JsonPropertyName("Characters")]
        public CharacterContext Characters { get; set; } = new();

        [JsonPropertyName("Factions")]
        public FactionsContext Factions { get; set; } = new();

        [JsonPropertyName("Locations")]
        public LocationContext Locations { get; set; } = new();

        [JsonPropertyName("PlotRules")]
        public List<PlotRulesData> PlotRules { get; set; } = new();
    }
}

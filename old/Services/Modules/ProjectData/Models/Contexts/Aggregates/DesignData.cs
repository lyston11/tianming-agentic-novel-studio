using TM.Services.Modules.ProjectData.Models.Contexts.Design;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Aggregates
{
    public class DesignData
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

        [JsonPropertyName("Plot")]
        public PlotContext Plot { get; set; } = new();
    }
}

using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Design
{
    public class LocationContext
    {
        [JsonPropertyName("Templates")]
        public TemplatesContext Templates { get; set; } = new();

        [JsonPropertyName("Worldview")]
        public WorldviewContext Worldview { get; set; } = new();

        [JsonPropertyName("Characters")]
        public CharacterContext Characters { get; set; } = new();

        [JsonPropertyName("Factions")]
        public FactionsContext Factions { get; set; } = new();

        [JsonPropertyName("LocationRules")]
        public List<LocationRulesData> LocationRules { get; set; } = new();
    }
}

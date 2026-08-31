using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Design
{
    public class FactionsContext
    {
        [JsonPropertyName("Templates")]
        public TemplatesContext Templates { get; set; } = new();

        [JsonPropertyName("Worldview")]
        public WorldviewContext Worldview { get; set; } = new();

        [JsonPropertyName("Characters")]
        public CharacterContext Characters { get; set; } = new();

        [JsonPropertyName("FactionRules")]
        public List<FactionRulesData> FactionRules { get; set; } = new();
    }
}

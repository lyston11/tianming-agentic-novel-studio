using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Design
{
    public class CharacterContext
    {
        [JsonPropertyName("Templates")]
        public TemplatesContext Templates { get; set; } = new();

        [JsonPropertyName("Worldview")]
        public WorldviewContext Worldview { get; set; } = new();

        [JsonPropertyName("CharacterRules")]
        public List<CharacterRulesData> CharacterRules { get; set; } = new();
    }
}

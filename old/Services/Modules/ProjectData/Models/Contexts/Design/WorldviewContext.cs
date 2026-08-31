using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Design
{
    public class WorldviewContext
    {
        [JsonPropertyName("Templates")]
        public TemplatesContext Templates { get; set; } = new();

        [JsonPropertyName("WorldRules")]
        public List<WorldRulesData> WorldRules { get; set; } = new();
    }
}

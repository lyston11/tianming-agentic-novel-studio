using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Contexts.Aggregates;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Generate
{
    public class OutlineContext
    {
        [JsonPropertyName("Design")]
        public DesignData Design { get; set; } = new();

        [JsonPropertyName("Outlines")]
        public List<OutlineData> Outlines { get; set; } = new();
    }
}

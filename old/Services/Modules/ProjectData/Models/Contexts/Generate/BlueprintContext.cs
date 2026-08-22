using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Contexts.Aggregates;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Generate
{
    public class BlueprintContext
    {
        [JsonPropertyName("Design")]
        public DesignData Design { get; set; } = new();

        [JsonPropertyName("Outline")]
        public OutlineDataAggregate Outline { get; set; } = new();

        [JsonPropertyName("Planning")]
        public PlanningData Planning { get; set; } = new();

        [JsonPropertyName("Blueprints")]
        public List<BlueprintData> Blueprints { get; set; } = new();
    }
}

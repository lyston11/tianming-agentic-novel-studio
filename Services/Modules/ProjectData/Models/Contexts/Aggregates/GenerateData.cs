using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Aggregates
{
    public class GenerateData
    {
        [JsonPropertyName("Outline")]
        public OutlineDataAggregate Outline { get; set; } = new();

        [JsonPropertyName("Planning")]
        public PlanningData Planning { get; set; } = new();

        [JsonPropertyName("Blueprint")]
        public BlueprintDataAggregate Blueprint { get; set; } = new();

        [JsonPropertyName("VolumeDesign")]
        public VolumeDesignDataAggregate VolumeDesign { get; set; } = new();
    }

    public class OutlineDataAggregate
    {
        [JsonPropertyName("Outlines")]
        public List<Models.Generate.StrategicOutline.OutlineData> Outlines { get; set; } = new();
    }

    public class PlanningData
    {
        [JsonPropertyName("Chapters")]
        public List<ChapterData> Chapters { get; set; } = new();
    }

    public class BlueprintDataAggregate
    {
        [JsonPropertyName("Blueprints")]
        public List<Models.Generate.ChapterBlueprint.BlueprintData> Blueprints { get; set; } = new();
    }

    public class VolumeDesignDataAggregate
    {
        [JsonPropertyName("VolumeDesigns")]
        public List<VolumeDesignData> VolumeDesigns { get; set; } = new();
    }
}

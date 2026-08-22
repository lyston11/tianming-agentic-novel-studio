using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Contexts.Aggregates;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Generate
{
    public class PlanningContext
    {
        [JsonPropertyName("Design")]
        public DesignData Design { get; set; } = new();

        [JsonPropertyName("Outline")]
        public OutlineDataAggregate Outline { get; set; } = new();

        [JsonPropertyName("Chapters")]
        public List<ChapterData> Chapters { get; set; } = new();
    }
}

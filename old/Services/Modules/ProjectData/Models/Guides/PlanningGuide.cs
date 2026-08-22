using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class PlanningGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "PlanningGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Volumes")] public Dictionary<string, PlanningVolumeEntry> Volumes { get; set; } = new();
    }

    public class PlanningVolumeEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("VolumeNumber")] public int VolumeNumber { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ContextIds")] public ContextIdCollection ContextIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Chapters")] public Dictionary<string, PlanningChapterEntry> Chapters { get; set; } = new();
    }

    public class PlanningChapterEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("ChapterPlanId")] public string ChapterPlanId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ChapterNumber")] public int ChapterNumber { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Synopsis")] public string Synopsis { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Rhythm")] public RhythmInfo Rhythm { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("PlotAllocations")] public List<PlotAllocationEntry> PlotAllocations { get; set; } = new();
    }

    public class PlotAllocationEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("PlotPointId")] public string PlotPointId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PlotType")] public string PlotType { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Priority")] public int? Priority { get; set; }
    }
}

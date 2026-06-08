using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class OutlineGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "OutlineGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Volumes")] public Dictionary<string, VolumeGuideEntry> Volumes { get; set; } = new();
    }

    public class VolumeGuideEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("VolumeNumber")] public int VolumeNumber { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Theme")] public string Theme { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PlannedChapters")] public int PlannedChapters { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ContextIds")] public ContextIdCollection ContextIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("OutputIds")] public OutputIds OutputIds { get; set; } = new();
    }

    public class OutputIds
    {
        [System.Text.Json.Serialization.JsonPropertyName("KeyEvents")] public List<string> KeyEvents { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ForeshadowingSetup")] public List<string> ForeshadowingSetup { get; set; } = new();
    }
}

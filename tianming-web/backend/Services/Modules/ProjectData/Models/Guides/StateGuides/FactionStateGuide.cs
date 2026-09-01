using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class FactionStateGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "FactionStateGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Factions")] public Dictionary<string, FactionStateEntry> Factions { get; set; } = new();
    }

    public class FactionStateEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CurrentStatus")] public string CurrentStatus { get; set; } = "active";
        [System.Text.Json.Serialization.JsonPropertyName("StateHistory")] public List<FactionStatePoint> StateHistory { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("DriftWarnings")] public List<string> DriftWarnings { get; set; } = new();
    }

    public class FactionStatePoint
    {
        [System.Text.Json.Serialization.JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Status")] public string Status { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Importance")] public string Importance { get; set; } = "normal"; [System.Text.Json.Serialization.JsonPropertyName("CausedBy")] public string CausedBy { get; set; } = string.Empty;
    }
}

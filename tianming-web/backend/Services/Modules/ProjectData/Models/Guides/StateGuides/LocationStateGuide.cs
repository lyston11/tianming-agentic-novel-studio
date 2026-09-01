using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class LocationStateGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "LocationStateGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Locations")] public Dictionary<string, LocationStateEntry> Locations { get; set; } = new();
    }

    public class LocationStateEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CurrentStatus")] public string CurrentStatus { get; set; } = "normal";
        [System.Text.Json.Serialization.JsonPropertyName("StateHistory")] public List<LocationStatePoint> StateHistory { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("DriftWarnings")] public List<string> DriftWarnings { get; set; } = new();
    }

    public class LocationStatePoint
    {
        [System.Text.Json.Serialization.JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Status")] public string Status { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }
}

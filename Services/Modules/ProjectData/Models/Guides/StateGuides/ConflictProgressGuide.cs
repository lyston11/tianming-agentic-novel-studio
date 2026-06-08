using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class ConflictProgressGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "ConflictProgressGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Conflicts")] public Dictionary<string, ConflictProgressEntry> Conflicts { get; set; } = new();
    }

    public class ConflictProgressEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Type")] public string Type { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Tier")] public string Tier { get; set; } = "Tier-3";
        [System.Text.Json.Serialization.JsonPropertyName("Status")] public string Status { get; set; } = "pending"; [System.Text.Json.Serialization.JsonPropertyName("ProgressPoints")] public List<ConflictProgressPoint> ProgressPoints { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("InvolvedChapters")] public List<string> InvolvedChapters { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("InvolvedCharacters")] public List<string> InvolvedCharacters { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("DriftWarnings")] public List<string> DriftWarnings { get; set; } = new();
    }

    public class ConflictProgressPoint
    {
        [System.Text.Json.Serialization.JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Status")] public string Status { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Importance")] public string Importance { get; set; } = "normal"; [System.Text.Json.Serialization.JsonPropertyName("CausedBy")] public string CausedBy { get; set; } = string.Empty;
    }
}

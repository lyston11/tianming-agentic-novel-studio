using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class DeadlineConstraintGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "DeadlineConstraintGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Deadlines")] public Dictionary<string, DeadlineEntry> Deadlines { get; set; } = new();
    }

    public class DeadlineEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Type")] public string Type { get; set; } = "countdown";
        [System.Text.Json.Serialization.JsonPropertyName("CurrentStatus")] public string CurrentStatus { get; set; } = "active";
        [System.Text.Json.Serialization.JsonPropertyName("Deadline")] public string Deadline { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("TriggerCondition")] public string TriggerCondition { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Consequence")] public string Consequence { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PartyIds")] public List<string> PartyIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("History")] public List<DeadlineConstraintPoint> History { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("IsOverdue")] public bool IsOverdue { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DriftWarnings")] public List<string> DriftWarnings { get; set; } = new();
    }

    public class DeadlineConstraintPoint
    {
        [System.Text.Json.Serialization.JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Action")] public string Action { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("KeyEvent")] public string KeyEvent { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }
}

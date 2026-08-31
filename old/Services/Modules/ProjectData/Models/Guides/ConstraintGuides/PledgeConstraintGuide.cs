using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class PledgeConstraintGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "PledgeConstraintGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Pledges")] public Dictionary<string, PledgeEntry> Pledges { get; set; } = new();
    }

    public class PledgeEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Type")] public string Type { get; set; } = "pledge";
        [System.Text.Json.Serialization.JsonPropertyName("CurrentStatus")] public string CurrentStatus { get; set; } = "active";
        [System.Text.Json.Serialization.JsonPropertyName("PartyIds")] public List<string> PartyIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Condition")] public string Condition { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Consequence")] public string Consequence { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("History")] public List<PledgeConstraintPoint> History { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("IsOverdue")] public bool IsOverdue { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DriftWarnings")] public List<string> DriftWarnings { get; set; } = new();
    }

    public class PledgeConstraintPoint
    {
        [System.Text.Json.Serialization.JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Action")] public string Action { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("KeyEvent")] public string KeyEvent { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }
}

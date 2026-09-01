using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class SecretRevealGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "SecretRevealGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Secrets")] public Dictionary<string, SecretRevealEntry> Secrets { get; set; } = new();
    }

    public class SecretRevealEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("KnowerIds")] public List<string> KnowerIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("CurrentStatus")] public string CurrentStatus { get; set; } = "hidden";
        [System.Text.Json.Serialization.JsonPropertyName("RevealHistory")] public List<SecretRevealPoint> RevealHistory { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("DriftWarnings")] public List<string> DriftWarnings { get; set; } = new();
    }

    public class SecretRevealPoint
    {
        [System.Text.Json.Serialization.JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("NewKnowerIds")] public List<string> NewKnowerIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Method")] public string Method { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("KeyEvent")] public string KeyEvent { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Importance")] public string Importance { get; set; } = "normal"; [System.Text.Json.Serialization.JsonPropertyName("CausedBy")] public string CausedBy { get; set; } = string.Empty;
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ContextIdCollection
    {
        [System.Text.Json.Serialization.JsonPropertyName("VolumeOutline")] public string VolumeOutline { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("VolumeDesignId")] public string VolumeDesignId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ChapterPlanId")] public string ChapterPlanId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ChapterBlueprint")] public string ChapterBlueprint { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("BlueprintIds")] public List<string> BlueprintIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Characters")] public List<string> Characters { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Factions")] public List<string> Factions { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Locations")] public List<string> Locations { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("PlotRules")] public List<string> PlotRules { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("TemplateIds")] public List<string> TemplateIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("WorldRuleIds")] public List<string> WorldRuleIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Conflicts")] public List<string> Conflicts { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ForeshadowingSetups")] public List<string> ForeshadowingSetups { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ForeshadowingPayoffs")] public List<string> ForeshadowingPayoffs { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("PreviousChapter")] public string PreviousChapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PreviousVolumes")] public List<string> PreviousVolumes { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("PreviousOutlines")] public List<string> PreviousOutlines { get; set; } = new();
    }

    public class ContextIdValidationResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("IsValid")] public bool IsValid { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("MissingIds")] public Dictionary<string, List<string>> MissingIds { get; set; } = new();

        public static ContextIdValidationResult Success() => new() { IsValid = true };

        public static ContextIdValidationResult Failed(Dictionary<string, List<string>> missingIds)
            => new() { IsValid = false, MissingIds = missingIds };

        public string GetErrorSummary()
        {
            if (IsValid) return string.Empty;
            return string.Join("; ", MissingIds.Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}"));
        }
    }
}

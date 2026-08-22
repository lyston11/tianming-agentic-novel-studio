using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class PlotPointsIndex
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "PlotPointsIndex";
        [System.Text.Json.Serialization.JsonPropertyName("PlotPoints")] public List<PlotPointEntry> PlotPoints { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Keywords")] public Dictionary<string, KeywordEntry> Keywords { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ChapterIndex")] public Dictionary<string, List<string>> ChapterIndex { get; set; } = new();
    }

    public class KeywordEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Appearances")] public List<KeywordAppearance> Appearances { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("RelatedForeshadowings")] public List<string> RelatedForeshadowings { get; set; } = new();
    }

    public class KeywordAppearance
    {
        [System.Text.Json.Serialization.JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Context")] public string Context { get; set; } = string.Empty;
    }
}

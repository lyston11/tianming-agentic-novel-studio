using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Index
{
    public class UpstreamIndex
    {
        [JsonPropertyName("SmartParsing")] public List<IndexItem> SmartParsing { get; set; } = new();
        [JsonPropertyName("Templates")] public List<IndexItem> Templates { get; set; } = new();
        [JsonPropertyName("Worldview")] public List<IndexItem> Worldview { get; set; } = new();
        [JsonPropertyName("Characters")] public List<IndexItem> Characters { get; set; } = new();
        [JsonPropertyName("Factions")] public List<IndexItem> Factions { get; set; } = new();
        [JsonPropertyName("Locations")] public List<IndexItem> Locations { get; set; } = new();
        [JsonPropertyName("Plot")] public List<IndexItem> Plot { get; set; } = new();

        [JsonPropertyName("Outline")] public List<IndexItem> Outline { get; set; } = new();
        [JsonPropertyName("Planning")] public List<IndexItem> Planning { get; set; } = new();
        [JsonPropertyName("Blueprint")] public List<IndexItem> Blueprint { get; set; } = new();
    }
}

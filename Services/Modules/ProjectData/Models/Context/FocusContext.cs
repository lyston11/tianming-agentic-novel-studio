using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Index;

namespace TM.Services.Modules.ProjectData.Models.Context
{
    public class FocusContext
    {
        [System.Text.Json.Serialization.JsonPropertyName("FocusId")] public string FocusId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("FocusType")] public string FocusType { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Layer")] public string Layer { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("FocusEntity")] public object? FocusEntity { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DirectRelations")] public List<IndexItem> DirectRelations { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("IndirectRelations")] public List<IndexItem> IndirectRelations { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("UpstreamIndex")] public UpstreamIndex UpstreamIndex { get; set; } = new();
    }

    public class DesignFocusContext
    {
        [System.Text.Json.Serialization.JsonPropertyName("GlobalSummary")] public GlobalSummary GlobalSummary { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("TrackingStatus")] public TrackingStatus TrackingStatus { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("UpstreamIndex")] public UpstreamIndex UpstreamIndex { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Focus")] public FocusContext Focus { get; set; } = new();
    }

    public class GenerateFocusContext
    {
        [System.Text.Json.Serialization.JsonPropertyName("GlobalSummary")] public GlobalSummary GlobalSummary { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("TrackingStatus")] public TrackingStatus TrackingStatus { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("UpstreamIndex")] public UpstreamIndex UpstreamIndex { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Focus")] public FocusContext Focus { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("TaskContext")] public object? TaskContext { get; set; }
    }
}

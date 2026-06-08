using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class SceneGuideEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("SceneId")] public string SceneId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("SceneNumber")] public int SceneNumber { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Purpose")] public string Purpose { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PovCharacter")] public string PovCharacter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Opening")] public string Opening { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Development")] public string Development { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Turning")] public string Turning { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Ending")] public string Ending { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("InfoDrop")] public string InfoDrop { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CharacterIds")] public List<string> CharacterIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("LocationId")] public string LocationId { get; set; } = string.Empty;
    }
}

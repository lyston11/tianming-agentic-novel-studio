using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Context
{
    public class ExpansionConfig
    {
        [System.Text.Json.Serialization.JsonPropertyName("Enabled")] public bool Enabled { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("Rules")] public ExpansionRules Rules { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Limits")] public ExpansionLimits Limits { get; set; } = new();
    }

    public class ExpansionRules
    {
        [System.Text.Json.Serialization.JsonPropertyName("SceneCharactersThreshold")] public int SceneCharactersThreshold { get; set; } = 5;
        [System.Text.Json.Serialization.JsonPropertyName("TriggerKeywords")] public List<string> TriggerKeywords { get; set; } = new() { "高潮", "转折", "决战", "真相", "回收" };
    }

    public class ExpansionLimits
    {
        [System.Text.Json.Serialization.JsonPropertyName("MaxAdditionalCharacters")] public int MaxAdditionalCharacters { get; set; } = 5;
        [System.Text.Json.Serialization.JsonPropertyName("MaxAdditionalForeshadowings")] public int MaxAdditionalForeshadowings { get; set; } = 3;
    }
}

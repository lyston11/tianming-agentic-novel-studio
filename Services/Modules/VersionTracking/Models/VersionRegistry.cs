using System.Collections.Generic;

namespace TM.Services.Modules.VersionTracking.Models
{
    public class VersionRegistry
    {
        [System.Text.Json.Serialization.JsonPropertyName("ModuleVersions")] public Dictionary<string, int> ModuleVersions { get; set; } = new();
    }
}

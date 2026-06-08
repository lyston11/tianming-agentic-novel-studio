using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public class ProtocolValidationResult
    {
        [JsonPropertyName("Success")] public bool Success { get; set; }
        [JsonPropertyName("Errors")] public List<string> Errors { get; set; } = new();
        [JsonPropertyName("Changes")] public ChapterChanges? Changes { get; set; }
        [JsonPropertyName("ContentWithoutChanges")] public string? ContentWithoutChanges { get; set; }

        public void AddError(string error)
        {
            Errors.Add(error);
            Success = false;
        }

        public bool HasErrors => Errors.Count > 0;
    }
}

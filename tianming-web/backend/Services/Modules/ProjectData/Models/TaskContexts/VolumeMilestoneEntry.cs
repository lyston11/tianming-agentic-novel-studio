using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.TaskContexts
{
    public class VolumeMilestoneEntry
    {
        [JsonPropertyName("VolumeNumber")]
        public int VolumeNumber { get; set; }

        [JsonPropertyName("Milestone")]
        public string Milestone { get; set; } = string.Empty;
    }
}

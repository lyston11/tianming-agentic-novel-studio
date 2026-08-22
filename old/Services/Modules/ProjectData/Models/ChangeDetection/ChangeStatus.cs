using System;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.ChangeDetection
{
    public class ChangeStatus
    {
        [JsonPropertyName("ModulePath")] public string ModulePath { get; set; } = string.Empty;
        [JsonPropertyName("DisplayName")] public string DisplayName { get; set; } = string.Empty;
        [JsonPropertyName("Status")] public ChangeStatusType Status { get; set; } = ChangeStatusType.Never;
        [JsonPropertyName("LastModified")] public DateTime? LastModified { get; set; }
        [JsonPropertyName("LastPackaged")] public DateTime? LastPackaged { get; set; }
        [JsonPropertyName("ItemCount")] public int ItemCount { get; set; }
        [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
    }

    public enum ChangeStatusType
    {
        Latest,

        Changed,

        Never
    }
}

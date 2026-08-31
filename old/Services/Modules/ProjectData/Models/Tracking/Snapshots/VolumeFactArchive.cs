using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public class VolumeFactArchive
    {
        [JsonPropertyName("VolumeNumber")] public int VolumeNumber { get; set; }
        [JsonPropertyName("LastChapterId")] public string LastChapterId { get; set; } = string.Empty;
        [JsonPropertyName("ArchivedAt")] public DateTime ArchivedAt { get; set; }
        [JsonPropertyName("CharacterStates")] public List<CharacterStateSnapshot> CharacterStates { get; set; } = new();
        [JsonPropertyName("ConflictProgress")] public List<ConflictProgressSnapshot> ConflictProgress { get; set; } = new();
        [JsonPropertyName("ForeshadowingStatus")] public List<ForeshadowingStatusSnapshot> ForeshadowingStatus { get; set; } = new();
        [JsonPropertyName("LocationStates")] public List<LocationStateSnapshot> LocationStates { get; set; } = new();
        [JsonPropertyName("FactionStates")] public List<FactionStateSnapshot> FactionStates { get; set; } = new();
        [JsonPropertyName("ItemStates")] public List<ItemStateSnapshot> ItemStates { get; set; } = new();
        [JsonPropertyName("SecretStates")] public List<SecretStateSnapshot> SecretStates { get; set; } = new();
        [JsonPropertyName("Timeline")] public List<TimelineSnapshot> Timeline { get; set; } = new();
        [JsonPropertyName("CharacterLocations")] public List<CharacterLocationSnapshot> CharacterLocations { get; set; } = new();
        [JsonPropertyName("PledgeStates")] public List<PledgeStateSnapshot> PledgeStates { get; set; } = new();
        [JsonPropertyName("DeadlineStates")] public List<DeadlineStateSnapshot> DeadlineStates { get; set; } = new();
    }
}

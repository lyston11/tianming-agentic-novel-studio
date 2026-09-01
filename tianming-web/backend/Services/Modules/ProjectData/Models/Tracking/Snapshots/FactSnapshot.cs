using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public class FactSnapshot
    {
        [JsonPropertyName("CharacterStates")] public List<CharacterStateSnapshot> CharacterStates { get; set; } = new();
        [JsonPropertyName("ConflictProgress")] public List<ConflictProgressSnapshot> ConflictProgress { get; set; } = new();
        [JsonPropertyName("ForeshadowingStatus")] public List<ForeshadowingStatusSnapshot> ForeshadowingStatus { get; set; } = new();
        [JsonPropertyName("PlotPoints")] public List<PlotPointSnapshot> PlotPoints { get; set; } = new();
        [JsonPropertyName("CharacterDescriptions")] public Dictionary<string, CharacterCoreDescription> CharacterDescriptions { get; set; } = new();
        [JsonPropertyName("LocationDescriptions")] public Dictionary<string, LocationCoreDescription> LocationDescriptions { get; set; } = new();
        [JsonPropertyName("WorldRuleConstraints")] public List<WorldRuleConstraint> WorldRuleConstraints { get; set; } = new();
        [JsonPropertyName("LocationStates")] public List<LocationStateSnapshot> LocationStates { get; set; } = new();
        [JsonPropertyName("FactionStates")] public List<FactionStateSnapshot> FactionStates { get; set; } = new();
        [JsonPropertyName("Timeline")] public List<TimelineSnapshot> Timeline { get; set; } = new();
        [JsonPropertyName("CharacterLocations")] public List<CharacterLocationSnapshot> CharacterLocations { get; set; } = new();
        [JsonPropertyName("ItemStates")] public List<ItemStateSnapshot> ItemStates { get; set; } = new();
        [JsonPropertyName("SecretStates")] public List<SecretStateSnapshot> SecretStates { get; set; } = new();
        [JsonPropertyName("PledgeStates")] public List<PledgeStateSnapshot> PledgeStates { get; set; } = new();
        [JsonPropertyName("DeadlineStates")] public List<DeadlineStateSnapshot> DeadlineStates { get; set; } = new();
    }

    public class CharacterCoreDescription
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("HairColor")] public string HairColor { get; set; } = string.Empty;
        [JsonPropertyName("EyeColor")] public string EyeColor { get; set; } = string.Empty;
        [JsonPropertyName("Appearance")] public string Appearance { get; set; } = string.Empty;
        [JsonPropertyName("PersonalityTags")] public List<string> PersonalityTags { get; set; } = new();
    }

    public class LocationCoreDescription
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("Features")] public List<string> Features { get; set; } = new();
    }

    public class WorldRuleConstraint
    {
        [JsonPropertyName("RuleId")] public string RuleId { get; set; } = string.Empty;
        [JsonPropertyName("RuleName")] public string RuleName { get; set; } = string.Empty;
        [JsonPropertyName("Constraint")] public string Constraint { get; set; } = string.Empty;
        [JsonPropertyName("IsHardConstraint")] public bool IsHardConstraint { get; set; } = true;
    }

    public class LocationStateSnapshot
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
    }

    public class FactionStateSnapshot
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
    }

    public class CharacterStateSnapshot
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Stage")] public string Stage { get; set; } = string.Empty;
        [JsonPropertyName("Abilities")] public string Abilities { get; set; } = string.Empty;
        [JsonPropertyName("Relationships")] public string Relationships { get; set; } = string.Empty;
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
    }

    public class ConflictProgressSnapshot
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("RecentProgress")] public List<string> RecentProgress { get; set; } = new();
    }

    public class ForeshadowingStatusSnapshot
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("IsSetup")] public bool IsSetup { get; set; }
        [JsonPropertyName("IsResolved")] public bool IsResolved { get; set; }
        [JsonPropertyName("IsOverdue")] public bool IsOverdue { get; set; }
        [JsonPropertyName("SetupChapterId")] public string? SetupChapterId { get; set; }
        [JsonPropertyName("PayoffChapterId")] public string? PayoffChapterId { get; set; }
    }

    public class PlotPointSnapshot
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("RelatedEntityIds")] public List<string> RelatedEntityIds { get; set; } = new();
        [JsonPropertyName("Storyline")] public string Storyline { get; set; } = "main";
    }

    public class TimelineSnapshot
    {
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("TimePeriod")] public string TimePeriod { get; set; } = string.Empty;
        [JsonPropertyName("ElapsedTime")] public string ElapsedTime { get; set; } = string.Empty;
        [JsonPropertyName("KeyTimeEvent")] public string KeyTimeEvent { get; set; } = string.Empty;
    }

    public class ItemStateSnapshot
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("CurrentHolder")] public string CurrentHolder { get; set; } = string.Empty;
        [JsonPropertyName("Status")] public string Status { get; set; } = "active";
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
    }

    public class SecretStateSnapshot
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("KnowerIds")] public List<string> KnowerIds { get; set; } = new();
        [JsonPropertyName("Status")] public string Status { get; set; } = "hidden";
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
    }

    public class PledgeStateSnapshot
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Type")] public string Type { get; set; } = "pledge";
        [JsonPropertyName("Status")] public string Status { get; set; } = "active";
        [JsonPropertyName("PartyIds")] public string PartyIds { get; set; } = string.Empty;
        [JsonPropertyName("Condition")] public string Condition { get; set; } = string.Empty;
        [JsonPropertyName("Consequence")] public string Consequence { get; set; } = string.Empty;
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("IsOverdue")] public bool IsOverdue { get; set; }
    }

    public class DeadlineStateSnapshot
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Type")] public string Type { get; set; } = "countdown";
        [JsonPropertyName("Status")] public string Status { get; set; } = "active";
        [JsonPropertyName("Deadline")] public string Deadline { get; set; } = string.Empty;
        [JsonPropertyName("TriggerCondition")] public string TriggerCondition { get; set; } = string.Empty;
        [JsonPropertyName("Consequence")] public string Consequence { get; set; } = string.Empty;
        [JsonPropertyName("PartyIds")] public string PartyIds { get; set; } = string.Empty;
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("IsOverdue")] public bool IsOverdue { get; set; }
    }

    public class CharacterLocationSnapshot
    {
        [JsonPropertyName("CharacterId")] public string CharacterId { get; set; } = string.Empty;
        [JsonPropertyName("CharacterName")] public string CharacterName { get; set; } = string.Empty;
        [JsonPropertyName("CurrentLocation")] public string CurrentLocation { get; set; } = string.Empty;
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
    }
}

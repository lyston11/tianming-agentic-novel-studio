using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public class CharacterStateChange
    {
        [JsonPropertyName("CharacterId")] public string CharacterId { get; set; } = string.Empty;
        [JsonPropertyName("NewLevel")] public string NewLevel { get; set; } = string.Empty;
        [JsonPropertyName("NewAbilities")] public List<string> NewAbilities { get; set; } = new();
        [JsonPropertyName("LostAbilities")] public List<string> LostAbilities { get; set; } = new();
        [JsonPropertyName("RelationshipChanges")] public Dictionary<string, RelationshipChange> RelationshipChanges { get; set; } = new();
        [JsonPropertyName("NewMentalState")] public string NewMentalState { get; set; } = string.Empty;
        [JsonPropertyName("KeyEvent")] public string KeyEvent { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
        [JsonPropertyName("CausedBy")] public string CausedBy { get; set; } = string.Empty;
    }

    public class RelationshipChange
    {
        [JsonPropertyName("Relation")] public string Relation { get; set; } = string.Empty;
        [JsonPropertyName("TrustDelta")] public int TrustDelta { get; set; }
        [JsonPropertyName("EmotionPhase")] public string EmotionPhase { get; set; } = string.Empty;
    }

    public class ConflictProgressChange
    {
        [JsonPropertyName("ConflictId")] public string ConflictId { get; set; } = string.Empty;
        [JsonPropertyName("NewStatus")] public string NewStatus { get; set; } = string.Empty;
        [JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
        [JsonPropertyName("CausedBy")] public string CausedBy { get; set; } = string.Empty;
    }

    public class PlotPointChange
    {
        [JsonPropertyName("Keywords")] public List<string> Keywords { get; set; } = new();
        [JsonPropertyName("Context")] public string Context { get; set; } = string.Empty;
        [JsonPropertyName("InvolvedCharacters")] public List<string> InvolvedCharacters { get; set; } = new();
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
        [JsonPropertyName("Storyline")] public string Storyline { get; set; } = "main";
        [JsonPropertyName("CausedBy")] public string CausedBy { get; set; } = string.Empty;
    }

    public class ForeshadowingAction
    {
        [JsonPropertyName("ForeshadowId")] public string ForeshadowId { get; set; } = string.Empty;
        [JsonPropertyName("Action")] public string Action { get; set; } = string.Empty;
    }

    public class LocationStateChange
    {
        [JsonPropertyName("LocationId")] public string LocationId { get; set; } = string.Empty;
        [JsonPropertyName("LocationName")] public string LocationName { get; set; } = string.Empty;
        [JsonPropertyName("NewStatus")] public string NewStatus { get; set; } = string.Empty;
        [JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class FactionStateChange
    {
        [JsonPropertyName("FactionId")] public string FactionId { get; set; } = string.Empty;
        [JsonPropertyName("NewStatus")] public string NewStatus { get; set; } = string.Empty;
        [JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
        [JsonPropertyName("CausedBy")] public string CausedBy { get; set; } = string.Empty;
    }

    public class TimeProgressionChange
    {
        [JsonPropertyName("TimePeriod")] public string TimePeriod { get; set; } = string.Empty;
        [JsonPropertyName("ElapsedTime")] public string ElapsedTime { get; set; } = string.Empty;
        [JsonPropertyName("KeyTimeEvent")] public string KeyTimeEvent { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class CharacterMovementChange
    {
        [JsonPropertyName("CharacterId")] public string CharacterId { get; set; } = string.Empty;
        [JsonPropertyName("FromLocation")] public string FromLocation { get; set; } = string.Empty;
        [JsonPropertyName("ToLocation")] public string ToLocation { get; set; } = string.Empty;
        [JsonPropertyName("ToLocationName")] public string ToLocationName { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class SecretRevealChange
    {
        [JsonPropertyName("SecretId")] public string SecretId { get; set; } = string.Empty;
        [JsonPropertyName("SecretName")] public string SecretName { get; set; } = string.Empty;
        [JsonPropertyName("NewKnowerIds")] public List<string> NewKnowerIds { get; set; } = new();
        [JsonPropertyName("Method")] public string Method { get; set; } = string.Empty;
        [JsonPropertyName("KeyEvent")] public string KeyEvent { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
        [JsonPropertyName("CausedBy")] public string CausedBy { get; set; } = string.Empty;
    }

    public class ItemTransferChange
    {
        [JsonPropertyName("ItemId")] public string ItemId { get; set; } = string.Empty;
        [JsonPropertyName("ItemName")] public string ItemName { get; set; } = string.Empty;
        [JsonPropertyName("FromHolder")] public string FromHolder { get; set; } = string.Empty;
        [JsonPropertyName("ToHolder")] public string ToHolder { get; set; } = string.Empty;
        [JsonPropertyName("NewStatus")] public string NewStatus { get; set; } = "active";
        [JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
        [JsonPropertyName("CausedBy")] public string CausedBy { get; set; } = string.Empty;
    }

    public class ChapterChanges
    {
        public const string ChangesSeparator = "---CHANGES---";

        public const string ChangesXmlOpen = "<chapter_changes>";

        public const string ChangesXmlClose = "</chapter_changes>";

        public static IReadOnlyList<string> TopLevelFieldNames { get; } = new[]
        {
            "CharacterStateChanges",
            "ConflictProgress",
            "NewPlotPoints",
            "ForeshadowingActions",
            "LocationStateChanges",
            "FactionStateChanges",
            "TimeProgression",
            "CharacterMovements",
            "ItemTransfers",
            "SecretRevealChanges",
            "PledgeConstraintChanges",
            "DeadlineConstraintChanges"
        };
        public static int TopLevelFieldCount => TopLevelFieldNames.Count;

        [JsonPropertyName("CharacterStateChanges")] public List<CharacterStateChange> CharacterStateChanges { get; set; } = new();
        [JsonPropertyName("ConflictProgress")] public List<ConflictProgressChange> ConflictProgress { get; set; } = new();
        [JsonPropertyName("NewPlotPoints")] public List<PlotPointChange> NewPlotPoints { get; set; } = new();
        [JsonPropertyName("ForeshadowingActions")] public List<ForeshadowingAction> ForeshadowingActions { get; set; } = new();
        [JsonPropertyName("LocationStateChanges")] public List<LocationStateChange> LocationStateChanges { get; set; } = new();
        [JsonPropertyName("FactionStateChanges")] public List<FactionStateChange> FactionStateChanges { get; set; } = new();
        [JsonPropertyName("TimeProgression")] public TimeProgressionChange? TimeProgression { get; set; }
        [JsonPropertyName("CharacterMovements")] public List<CharacterMovementChange> CharacterMovements { get; set; } = new();
        [JsonPropertyName("ItemTransfers")] public List<ItemTransferChange> ItemTransfers { get; set; } = new();
        [JsonPropertyName("SecretRevealChanges")] public List<SecretRevealChange> SecretRevealChanges { get; set; } = new();
        [JsonPropertyName("PledgeConstraintChanges")] public List<PledgeConstraintChange> PledgeConstraintChanges { get; set; } = new();
        [JsonPropertyName("DeadlineConstraintChanges")] public List<DeadlineConstraintChange> DeadlineConstraintChanges { get; set; } = new();
    }

    public class PledgeConstraintChange
    {
        [JsonPropertyName("PledgeId")] public string PledgeId { get; set; } = string.Empty;
        [JsonPropertyName("PledgeName")] public string PledgeName { get; set; } = string.Empty;
        [JsonPropertyName("Action")] public string Action { get; set; } = string.Empty;
        [JsonPropertyName("Type")] public string Type { get; set; } = "pledge";
        [JsonPropertyName("PartyIds")] public List<string> PartyIds { get; set; } = new();
        [JsonPropertyName("Condition")] public string Condition { get; set; } = string.Empty;
        [JsonPropertyName("Consequence")] public string Consequence { get; set; } = string.Empty;
        [JsonPropertyName("KeyEvent")] public string KeyEvent { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class DeadlineConstraintChange
    {
        [JsonPropertyName("DeadlineId")] public string DeadlineId { get; set; } = string.Empty;
        [JsonPropertyName("DeadlineName")] public string DeadlineName { get; set; } = string.Empty;
        [JsonPropertyName("Action")] public string Action { get; set; } = string.Empty;
        [JsonPropertyName("Type")] public string Type { get; set; } = "countdown";
        [JsonPropertyName("Deadline")] public string Deadline { get; set; } = string.Empty;
        [JsonPropertyName("TriggerCondition")] public string TriggerCondition { get; set; } = string.Empty;
        [JsonPropertyName("Consequence")] public string Consequence { get; set; } = string.Empty;
        [JsonPropertyName("PartyIds")] public List<string> PartyIds { get; set; } = new();
        [JsonPropertyName("KeyEvent")] public string KeyEvent { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class PlotPointEntry
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [JsonPropertyName("Keywords")] public List<string> Keywords { get; set; } = new();
        [JsonPropertyName("Context")] public string Context { get; set; } = string.Empty;
        [JsonPropertyName("InvolvedCharacters")] public List<string> InvolvedCharacters { get; set; } = new();
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
        [JsonPropertyName("Storyline")] public string Storyline { get; set; } = "main";
        [JsonPropertyName("CausedBy")] public string CausedBy { get; set; } = string.Empty;
    }

    public class ForeshadowingStatistics
    {
        [JsonPropertyName("TotalCount")] public int TotalCount { get; set; }
        [JsonPropertyName("SetupCount")] public int SetupCount { get; set; }
        [JsonPropertyName("ResolvedCount")] public int ResolvedCount { get; set; }
        [JsonPropertyName("OverdueCount")] public int OverdueCount { get; set; }
        [JsonPropertyName("Tier1Stats")] public ForeshadowingTierStats Tier1Stats { get; set; } = new();
        [JsonPropertyName("Tier2Stats")] public ForeshadowingTierStats Tier2Stats { get; set; } = new();
        [JsonPropertyName("Tier3Stats")] public ForeshadowingTierStats Tier3Stats { get; set; } = new();
    }

    public class ForeshadowingTierStats
    {
        [JsonPropertyName("Total")] public int Total { get; set; }
        [JsonPropertyName("Setup")] public int Setup { get; set; }
        [JsonPropertyName("Resolved")] public int Resolved { get; set; }
        [JsonPropertyName("Overdue")] public int Overdue { get; set; }
    }
}

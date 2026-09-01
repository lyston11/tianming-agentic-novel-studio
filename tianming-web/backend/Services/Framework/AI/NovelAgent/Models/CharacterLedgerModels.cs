using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class CharacterLedgerEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("characterName")]
        public string CharacterName { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public CharacterLedgerEntryType Type { get; set; } = CharacterLedgerEntryType.Goal;

        [JsonPropertyName("status")]
        public CharacterLedgerStatus Status { get; set; } = CharacterLedgerStatus.Proposed;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("currentGoal")]
        public string CurrentGoal { get; set; } = string.Empty;

        [JsonPropertyName("currentIntent")]
        public string CurrentIntent { get; set; } = string.Empty;

        [JsonPropertyName("nextPressure")]
        public string NextPressure { get; set; } = string.Empty;

        [JsonPropertyName("secret")]
        public CharacterSecretState Secret { get; set; } = new();

        [JsonPropertyName("relationship")]
        public CharacterRelationshipState Relationship { get; set; } = new();

        [JsonPropertyName("abilityCost")]
        public CharacterAbilityCostState AbilityCost { get; set; } = new();

        [JsonPropertyName("psychology")]
        public CharacterPsychologyState Psychology { get; set; } = new();

        [JsonPropertyName("beliefShift")]
        public string BeliefShift { get; set; } = string.Empty;

        [JsonPropertyName("identityState")]
        public string IdentityState { get; set; } = string.Empty;

        [JsonPropertyName("importance")]
        public int Importance { get; set; } = 5;

        [JsonPropertyName("evidence")]
        public List<string> Evidence { get; set; } = new();

        [JsonPropertyName("notes")]
        public List<string> Notes { get; set; } = new();

        [JsonPropertyName("sourceRunId")]
        public string SourceRunId { get; set; } = string.Empty;

        [JsonPropertyName("sourceChapterId")]
        public string SourceChapterId { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public sealed class CharacterSecretState
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public CharacterSecretStatus Status { get; set; } = CharacterSecretStatus.None;

        [JsonPropertyName("knownBy")]
        public List<string> KnownBy { get; set; } = new();
    }

    public sealed class CharacterRelationshipState
    {
        [JsonPropertyName("targetCharacter")]
        public string TargetCharacter { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public CharacterRelationshipStatus Status { get; set; } = CharacterRelationshipStatus.Unknown;

        [JsonPropertyName("tension")]
        public string Tension { get; set; } = string.Empty;

        [JsonPropertyName("change")]
        public string Change { get; set; } = string.Empty;
    }

    public sealed class CharacterAbilityCostState
    {
        [JsonPropertyName("ability")]
        public string Ability { get; set; } = string.Empty;

        [JsonPropertyName("levelOrBoundary")]
        public string LevelOrBoundary { get; set; } = string.Empty;

        [JsonPropertyName("cost")]
        public string Cost { get; set; } = string.Empty;

        [JsonPropertyName("debt")]
        public string Debt { get; set; } = string.Empty;

        [JsonPropertyName("limitation")]
        public string Limitation { get; set; } = string.Empty;
    }

    public sealed class CharacterPsychologyState
    {
        [JsonPropertyName("emotion")]
        public string Emotion { get; set; } = string.Empty;

        [JsonPropertyName("wound")]
        public string Wound { get; set; } = string.Empty;

        [JsonPropertyName("copingStrategy")]
        public string CopingStrategy { get; set; } = string.Empty;

        [JsonPropertyName("stressLevel")]
        public int StressLevel { get; set; } = 5;
    }

    public sealed class CharacterMaintenanceResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("requiresConfirmation")]
        public bool RequiresConfirmation { get; set; }

        [JsonPropertyName("riskLevel")]
        public NovelToolRiskLevel RiskLevel { get; set; } = NovelToolRiskLevel.Medium;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("importedEntries")]
        public List<CharacterLedgerEntry> ImportedEntries { get; set; } = new();

        [JsonPropertyName("updatedEntries")]
        public List<CharacterLedgerEntry> UpdatedEntries { get; set; } = new();

        [JsonPropertyName("conflictEntries")]
        public List<CharacterLedgerEntry> ConflictEntries { get; set; } = new();

        [JsonPropertyName("run")]
        public NovelAgentRun? Run { get; set; }

        [JsonPropertyName("document")]
        public StoryBibleDocument? Document { get; set; }
    }

    public enum CharacterLedgerEntryType
    {
        Goal = 0,
        Secret = 1,
        Relationship = 2,
        AbilityCost = 3,
        Psychology = 4,
        Belief = 5,
        Identity = 6,
        Role = 7,
        Death = 8,
        Other = 9
    }

    public enum CharacterLedgerStatus
    {
        Draft = 0,
        Proposed = 1,
        Active = 2,
        GoalUpdated = 3,
        SecretSeeded = 4,
        SecretRevealed = 5,
        RelationshipChanged = 6,
        RelationshipReversed = 7,
        AbilityChanged = 8,
        AbilityRuleChanged = 9,
        PsychologicalShifted = 10,
        BeliefShifted = 11,
        IdentityRewritten = 12,
        LeftStage = 13,
        Dead = 14,
        Conflict = 15,
        Rejected = 16
    }

    public enum CharacterSecretStatus
    {
        None = 0,
        Seeded = 1,
        Suspected = 2,
        PartiallyRevealed = 3,
        Revealed = 4,
        FalseLead = 5
    }

    public enum CharacterRelationshipStatus
    {
        Unknown = 0,
        Ally = 1,
        Rival = 2,
        Enemy = 3,
        Mentor = 4,
        Family = 5,
        Lover = 6,
        Betrayed = 7,
        Broken = 8,
        Ambiguous = 9
    }
}

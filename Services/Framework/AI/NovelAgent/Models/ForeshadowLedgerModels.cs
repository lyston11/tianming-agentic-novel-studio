using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class ForeshadowLedgerEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public ForeshadowLedgerEntryType Type { get; set; } = ForeshadowLedgerEntryType.Plot;

        [JsonPropertyName("status")]
        public ForeshadowLedgerStatus Status { get; set; } = ForeshadowLedgerStatus.Proposed;

        [JsonPropertyName("setup")]
        public string Setup { get; set; } = string.Empty;

        [JsonPropertyName("payoff")]
        public string Payoff { get; set; } = string.Empty;

        [JsonPropertyName("sourceVolumeId")]
        public string SourceVolumeId { get; set; } = string.Empty;

        [JsonPropertyName("plannedSetupChapterId")]
        public string PlannedSetupChapterId { get; set; } = string.Empty;

        [JsonPropertyName("plannedPayoffChapterId")]
        public string PlannedPayoffChapterId { get; set; } = string.Empty;

        [JsonPropertyName("actualSetupChapterIds")]
        public List<string> ActualSetupChapterIds { get; set; } = new();

        [JsonPropertyName("actualReinforceChapterIds")]
        public List<string> ActualReinforceChapterIds { get; set; } = new();

        [JsonPropertyName("actualPayoffChapterId")]
        public string ActualPayoffChapterId { get; set; } = string.Empty;

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

    public sealed class ForeshadowMaintenanceResult
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
        public List<ForeshadowLedgerEntry> ImportedEntries { get; set; } = new();

        [JsonPropertyName("updatedEntries")]
        public List<ForeshadowLedgerEntry> UpdatedEntries { get; set; } = new();

        [JsonPropertyName("conflictEntries")]
        public List<ForeshadowLedgerEntry> ConflictEntries { get; set; } = new();

        [JsonPropertyName("run")]
        public NovelAgentRun? Run { get; set; }

        [JsonPropertyName("document")]
        public StoryBibleDocument? Document { get; set; }
    }

    public enum ForeshadowLedgerEntryType
    {
        Plot = 0,
        WorldRule = 1,
        CharacterSecret = 2,
        Relationship = 3,
        Object = 4,
        Threat = 5,
        Theme = 6,
        Other = 7
    }

    public enum ForeshadowLedgerStatus
    {
        Draft = 0,
        Proposed = 1,
        Planned = 2,
        Setup = 3,
        Reinforced = 4,
        Due = 5,
        PaidOff = 6,
        Abandoned = 7,
        Conflict = 8
    }
}

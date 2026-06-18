using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class StoryBibleDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("constitution")]
        public StoryCreativeConstitution? Constitution { get; set; }

        [JsonPropertyName("macroCandidates")]
        public List<MacroStoryConceptCandidate> MacroCandidates { get; set; } = new();

        [JsonPropertyName("volumeArcs")]
        public List<VolumeArcPlan> VolumeArcs { get; set; } = new();

        [JsonPropertyName("foreshadowLedger")]
        public List<ForeshadowLedgerEntry> ForeshadowLedger { get; set; } = new();

        [JsonPropertyName("characterLedger")]
        public List<CharacterLedgerEntry> CharacterLedger { get; set; } = new();

        [JsonPropertyName("continuityFacts")]
        public List<ChapterContinuityFacts> ContinuityFacts { get; set; } = new();

        [JsonPropertyName("canonLedger")]
        public List<CanonLedgerEntry> CanonLedger { get; set; } = new();

        [JsonPropertyName("agentRuns")]
        public List<NovelAgentRun> AgentRuns { get; set; } = new();

        [JsonPropertyName("revisions")]
        public List<StoryBibleRevision> Revisions { get; set; } = new();

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public sealed class CanonLedgerEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("type")]
        public CanonLedgerEntryType Type { get; set; } = CanonLedgerEntryType.WorldRule;

        [JsonPropertyName("status")]
        public CanonLedgerEntryStatus Status { get; set; } = CanonLedgerEntryStatus.Proposed;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("rationale")]
        public string Rationale { get; set; } = string.Empty;

        [JsonPropertyName("impactScope")]
        public string ImpactScope { get; set; } = string.Empty;

        [JsonPropertyName("conflictCheck")]
        public string ConflictCheck { get; set; } = string.Empty;

        [JsonPropertyName("sourceRunId")]
        public string SourceRunId { get; set; } = string.Empty;

        [JsonPropertyName("sourceChapterId")]
        public string SourceChapterId { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public sealed class StoryBibleRevision
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("sourceRunId")]
        public string SourceRunId { get; set; } = string.Empty;

        [JsonPropertyName("sourceChapterId")]
        public string SourceChapterId { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public sealed class StoryBibleCommitResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("requiresOverwrite")]
        public bool RequiresOverwrite { get; set; }

        [JsonPropertyName("requiresConfirmation")]
        public bool RequiresConfirmation { get; set; }

        [JsonPropertyName("riskLevel")]
        public NovelToolRiskLevel RiskLevel { get; set; } = NovelToolRiskLevel.Low;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("storagePath")]
        public string StoragePath { get; set; } = string.Empty;

        [JsonPropertyName("document")]
        public StoryBibleDocument? Document { get; set; }
    }

    public enum CanonLedgerEntryType
    {
        WorldRule = 0,
        CharacterRule = 1,
        FactionRule = 2,
        LocationRule = 3,
        PlotRule = 4,
        Foreshadowing = 5,
        Theme = 6,
        Constraint = 7
    }

    public enum CanonLedgerEntryStatus
    {
        Draft = 0,
        Proposed = 1,
        Canon = 2,
        Deprecated = 3,
        Conflict = 4,
        Rejected = 5
    }
}

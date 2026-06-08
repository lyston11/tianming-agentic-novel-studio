using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class ChapterContextPackageSummary
    {
        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "pending";

        [JsonPropertyName("worldRules")]
        public List<string> WorldRules { get; set; } = new();

        [JsonPropertyName("characterStates")]
        public List<string> CharacterStates { get; set; } = new();

        [JsonPropertyName("activeConflicts")]
        public List<string> ActiveConflicts { get; set; } = new();

        [JsonPropertyName("activeForeshadowing")]
        public List<string> ActiveForeshadowing { get; set; } = new();

        [JsonPropertyName("chapterBlueprints")]
        public List<string> ChapterBlueprints { get; set; } = new();

        [JsonPropertyName("previousSummaries")]
        public List<string> PreviousSummaries { get; set; } = new();

        [JsonPropertyName("longDistanceRecall")]
        public List<string> LongDistanceRecall { get; set; } = new();

        [JsonPropertyName("ragQueries")]
        public List<string> RagQueries { get; set; } = new();

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();

        [JsonPropertyName("builtAt")]
        public DateTime BuiltAt { get; set; } = DateTime.Now;
    }

    public sealed class GenerationGateReport
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "pending";

        [JsonPropertyName("protocolPassed")]
        public bool ProtocolPassed { get; set; }

        [JsonPropertyName("changesDetected")]
        public bool ChangesDetected { get; set; }

        [JsonPropertyName("factSnapshotPassed")]
        public bool FactSnapshotPassed { get; set; }

        [JsonPropertyName("blueprintPassed")]
        public bool BlueprintPassed { get; set; }

        [JsonPropertyName("ragPassed")]
        public bool RagPassed { get; set; }

        [JsonPropertyName("issues")]
        public List<string> Issues { get; set; } = new();

        [JsonPropertyName("repairHints")]
        public List<string> RepairHints { get; set; } = new();

        [JsonPropertyName("validatedAt")]
        public DateTime ValidatedAt { get; set; } = DateTime.Now;
    }

    public sealed class ChapterDraftArtifact
    {
        [JsonPropertyName("artifactId")]
        public string ArtifactId { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "draft_generated";

        [JsonPropertyName("draftContent")]
        public string DraftContent { get; set; } = string.Empty;

        [JsonPropertyName("committedContent")]
        public string CommittedContent { get; set; } = string.Empty;

        [JsonPropertyName("changesJson")]
        public string ChangesJson { get; set; } = string.Empty;

        [JsonPropertyName("hasChanges")]
        public bool HasChanges { get; set; }

        [JsonPropertyName("repairAttemptCount")]
        public int RepairAttemptCount { get; set; }

        [JsonPropertyName("generatedAt")]
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("committedAt")]
        public DateTime? CommittedAt { get; set; }
    }

    public sealed class DependencyImpactReport
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "clean";

        [JsonPropertyName("changedModules")]
        public List<string> ChangedModules { get; set; } = new();

        [JsonPropertyName("impactedModules")]
        public List<string> ImpactedModules { get; set; } = new();

        [JsonPropertyName("impactedChapters")]
        public List<string> ImpactedChapters { get; set; } = new();

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}

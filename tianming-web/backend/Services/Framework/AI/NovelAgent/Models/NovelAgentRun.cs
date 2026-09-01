using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public enum NovelAgentIntent
    {
        Unknown = 0,
        CreateStoryFoundation = 1,
        RefineStoryFoundation = 2,
        PlanVolumeArc = 3,
        PlanChapter = 4,
        GenerateChapter = 5,
        RewriteChapter = 6,
        ValidateContinuity = 7,
        ExploreWorldbuilding = 8
    }

    public enum NovelAgentRunStatus
    {
        Draft = 0,
        Planning = 1,
        Retrieving = 2,
        AwaitingConfirmation = 3,
        Executing = 4,
        Validating = 5,
        Repairing = 6,
        Completed = 7,
        Failed = 8,
        Cancelled = 9
    }

    public enum NovelAgentStepStatus
    {
        Pending = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Skipped = 4,
        WaitingUser = 5
    }

    public sealed class NovelAgentRun
    {
        [JsonPropertyName("runId")]
        public string RunId { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("userGoal")]
        public string UserGoal { get; set; } = string.Empty;

        [JsonPropertyName("intent")]
        public NovelAgentIntent Intent { get; set; } = NovelAgentIntent.Unknown;

        [JsonPropertyName("status")]
        public NovelAgentRunStatus Status { get; set; } = NovelAgentRunStatus.Draft;

        [JsonPropertyName("targetChapterId")]
        public string TargetChapterId { get; set; } = string.Empty;

        [JsonPropertyName("storyConstitution")]
        public StoryCreativeConstitution? StoryConstitution { get; set; }

        [JsonPropertyName("macroCandidates")]
        public List<MacroStoryConceptCandidate> MacroCandidates { get; set; } = new();

        [JsonPropertyName("chapterBrief")]
        public ChapterCreativeBrief? ChapterBrief { get; set; }

        [JsonPropertyName("volumeArcPlan")]
        public VolumeArcPlan? VolumeArcPlan { get; set; }

        [JsonPropertyName("storyState")]
        public StoryStateSnapshot? StoryState { get; set; }

        [JsonPropertyName("contextPackage")]
        public ChapterContextPackageSummary? ContextPackage { get; set; }

        [JsonPropertyName("draftArtifact")]
        public ChapterDraftArtifact? DraftArtifact { get; set; }

        [JsonPropertyName("gateReport")]
        public GenerationGateReport? GateReport { get; set; }

        [JsonPropertyName("dependencyImpact")]
        public DependencyImpactReport? DependencyImpact { get; set; }

        [JsonPropertyName("postGenerationReview")]
        public NovelAgentPostGenerationReview? PostGenerationReview { get; set; }

        [JsonPropertyName("continuityFacts")]
        public ChapterContinuityFacts? ContinuityFacts { get; set; }

        [JsonPropertyName("steps")]
        public List<NovelAgentPlanStep> Steps { get; set; } = new();

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("notes")]
        public List<string> Notes { get; set; } = new();
    }

    public sealed class NovelAgentPlanStep
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("purpose")]
        public string Purpose { get; set; } = string.Empty;

        [JsonPropertyName("toolName")]
        public string ToolName { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public NovelAgentStepStatus Status { get; set; } = NovelAgentStepStatus.Pending;

        [JsonPropertyName("riskLevel")]
        public NovelToolRiskLevel RiskLevel { get; set; } = NovelToolRiskLevel.Low;

        [JsonPropertyName("requiresConfirmation")]
        public bool RequiresConfirmation { get; set; }

        [JsonPropertyName("inputs")]
        public Dictionary<string, string> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class NovelAgentRunOperationResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("run")]
        public NovelAgentRun? Run { get; set; }

        [JsonPropertyName("runs")]
        public List<NovelAgentRun> Runs { get; set; } = new();
    }

    public sealed class NovelAgentExecutionResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("requiresConfirmation")]
        public bool RequiresConfirmation { get; set; }

        [JsonPropertyName("riskLevel")]
        public NovelToolRiskLevel RiskLevel { get; set; } = NovelToolRiskLevel.Low;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("writerResult")]
        public string WriterResult { get; set; } = string.Empty;

        [JsonPropertyName("contextPackage")]
        public ChapterContextPackageSummary? ContextPackage { get; set; }

        [JsonPropertyName("draftArtifact")]
        public ChapterDraftArtifact? DraftArtifact { get; set; }

        [JsonPropertyName("gateReport")]
        public GenerationGateReport? GateReport { get; set; }

        [JsonPropertyName("dependencyImpact")]
        public DependencyImpactReport? DependencyImpact { get; set; }

        [JsonPropertyName("run")]
        public NovelAgentRun? Run { get; set; }
    }

    public enum NovelToolRiskLevel
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }
}

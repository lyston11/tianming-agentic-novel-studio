using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public enum NovelAgentReviewCheckStatus
    {
        Unknown = 0,
        Pass = 1,
        Warning = 2,
        Fail = 3
    }

    public sealed class NovelAgentPostGenerationReview
    {
        [JsonPropertyName("reviewId")]
        public string ReviewId { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("overallResult")]
        public string OverallResult { get; set; } = "Unknown";

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("qualityScore")]
        public int QualityScore { get; set; }

        [JsonPropertyName("contentLength")]
        public int ContentLength { get; set; }

        [JsonPropertyName("validationOverallResult")]
        public string ValidationOverallResult { get; set; } = string.Empty;

        [JsonPropertyName("validationIssueCount")]
        public int ValidationIssueCount { get; set; }

        [JsonPropertyName("requiresRewrite")]
        public bool RequiresRewrite { get; set; }

        [JsonPropertyName("checks")]
        public List<NovelAgentReviewCheck> Checks { get; set; } = new();

        [JsonPropertyName("storyVariableChanges")]
        public List<string> StoryVariableChanges { get; set; } = new();

        [JsonPropertyName("proposedCanonEntries")]
        public List<CanonLedgerEntry> ProposedCanonEntries { get; set; } = new();

        [JsonPropertyName("proposedForeshadowEntries")]
        public List<ForeshadowLedgerEntry> ProposedForeshadowEntries { get; set; } = new();

        [JsonPropertyName("proposedCharacterEntries")]
        public List<CharacterLedgerEntry> ProposedCharacterEntries { get; set; } = new();

        [JsonPropertyName("nextChapterSuggestions")]
        public List<string> NextChapterSuggestions { get; set; } = new();
    }

    public sealed class NovelAgentReviewCheck
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public NovelAgentReviewCheckStatus Status { get; set; } = NovelAgentReviewCheckStatus.Unknown;

        [JsonPropertyName("riskLevel")]
        public NovelToolRiskLevel RiskLevel { get; set; } = NovelToolRiskLevel.Low;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("evidence")]
        public List<string> Evidence { get; set; } = new();

        [JsonPropertyName("suggestions")]
        public List<string> Suggestions { get; set; } = new();
    }
}

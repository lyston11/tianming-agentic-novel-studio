using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class NovelAgentRewriteAttempt
    {
        [JsonPropertyName("attemptId")]
        public string AttemptId { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("beforeQualityScore")]
        public int BeforeQualityScore { get; set; }

        [JsonPropertyName("afterQualityScore")]
        public int AfterQualityScore { get; set; }

        [JsonPropertyName("repairHints")]
        public List<string> RepairHints { get; set; } = new();

        [JsonPropertyName("repairResult")]
        public string RepairResult { get; set; } = string.Empty;

        [JsonPropertyName("reviewAfterRewrite")]
        public NovelAgentPostGenerationReview? ReviewAfterRewrite { get; set; }
    }

    public sealed class NovelAgentRewriteResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("requiresConfirmation")]
        public bool RequiresConfirmation { get; set; }

        [JsonPropertyName("riskLevel")]
        public NovelToolRiskLevel RiskLevel { get; set; } = NovelToolRiskLevel.High;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("attempt")]
        public NovelAgentRewriteAttempt? Attempt { get; set; }

        [JsonPropertyName("run")]
        public NovelAgentRun? Run { get; set; }
    }
}

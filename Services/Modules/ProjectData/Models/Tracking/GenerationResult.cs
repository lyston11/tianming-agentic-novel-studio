using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public class GenerationResult
    {
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("CorrelationId")] public string CorrelationId { get; set; } = string.Empty;

        [JsonPropertyName("PromptTokenEstimate")] public int PromptTokenEstimate { get; set; }

        [JsonPropertyName("ContextBuildMs")] public long ContextBuildMs { get; set; }

        [JsonPropertyName("AiCallMs")] public long AiCallMs { get; set; }

        [JsonPropertyName("Success")] public bool Success { get; set; }

        [JsonPropertyName("Content")] public string? Content { get; set; }

        [JsonPropertyName("ParsedChanges")] public ChapterChanges? ParsedChanges { get; set; }

        [JsonPropertyName("GateResult")] public GateResult? GateResult { get; set; }

        [JsonPropertyName("DesignElements")] public DesignElementNames? DesignElements { get; set; }

        [JsonPropertyName("RequiresManualIntervention")] public bool RequiresManualIntervention { get; set; }

        [JsonPropertyName("InterventionHint")] public string InterventionHint { get; set; } = string.Empty;

        [JsonPropertyName("ErrorMessage")] public string ErrorMessage { get; set; } = string.Empty;

        [JsonPropertyName("Attempts")] public List<GenerationAttempt> Attempts { get; set; } = new();

        public int TotalAttempts => Attempts.Count;

        public int RewriteCount => Attempts.Count > 0 ? Attempts.Count - 1 : 0;

        public void AddAttempt(int attemptNumber, bool success, string message)
        {
            Attempts.Add(new GenerationAttempt
            {
                AttemptNumber = attemptNumber,
                Success = success,
                Message = message
            });
        }

        public void AddAttempt(int attemptNumber, bool success, string message, List<string> failureReasons)
        {
            Attempts.Add(new GenerationAttempt
            {
                AttemptNumber = attemptNumber,
                Success = success,
                Message = message,
                FailureReasons = failureReasons
            });
        }

        public List<string> GetLastFailureReasons()
        {
            if (Attempts.Count == 0) return new List<string>();
            return Attempts[Attempts.Count - 1].FailureReasons;
        }
    }

    public class GenerationAttempt
    {
        [JsonPropertyName("AttemptNumber")] public int AttemptNumber { get; set; }

        [JsonPropertyName("Success")] public bool Success { get; set; }

        [JsonPropertyName("Message")] public string Message { get; set; } = string.Empty;

        [JsonPropertyName("FailureReasons")] public List<string> FailureReasons { get; set; } = new();

        public bool IsRewrite => AttemptNumber > 0;
    }
}

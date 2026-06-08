using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public class GenerationStatistics
    {
        [JsonPropertyName("StartTime")] public DateTime StartTime { get; set; } = DateTime.Now;
        [JsonPropertyName("EndTime")] public DateTime EndTime { get; set; } = DateTime.Now;
        [JsonPropertyName("TotalGenerations")] public int TotalGenerations { get; set; }
        [JsonPropertyName("FirstPassCount")] public int FirstPassCount { get; set; }
        [JsonPropertyName("RewritePassCount")] public int RewritePassCount { get; set; }
        [JsonPropertyName("FinalFailureCount")] public int FinalFailureCount { get; set; }
        [JsonPropertyName("ProtocolFailureCount")] public int ProtocolFailureCount { get; set; }
        [JsonPropertyName("RuleFailureCount")] public int RuleFailureCount { get; set; }
        [JsonPropertyName("ConsistencyFailureCount")] public int ConsistencyFailureCount { get; set; }
        [JsonPropertyName("RewriteDistribution")] public Dictionary<int, int> RewriteDistribution { get; set; } = new();
        [JsonPropertyName("ConsistencyIssues")] public ConsistencyIssueStatistics ConsistencyIssues { get; set; } = new();

        public double FirstPassRate => TotalGenerations > 0
            ? (double)FirstPassCount / TotalGenerations * 100
            : 0;

        public double FinalPassRate => TotalGenerations > 0
            ? (double)(FirstPassCount + RewritePassCount) / TotalGenerations * 100
            : 0;

        public double AverageRewriteCount
        {
            get
            {
                var totalRewrites = 0;
                var rewriteGenerations = 0;
                foreach (var kvp in RewriteDistribution)
                {
                    if (kvp.Key > 0)
                    {
                        totalRewrites += kvp.Key * kvp.Value;
                        rewriteGenerations += kvp.Value;
                    }
                }
                return rewriteGenerations > 0 ? (double)totalRewrites / rewriteGenerations : 0;
            }
        }
    }

    public class ConsistencyIssueStatistics
    {
        [JsonPropertyName("CharacterStateConflict")] public int CharacterStateConflict { get; set; }
        [JsonPropertyName("ForeshadowingEarlyPayoff")] public int ForeshadowingEarlyPayoff { get; set; }
        [JsonPropertyName("ForeshadowingRollback")] public int ForeshadowingRollback { get; set; }
        [JsonPropertyName("ConflictStatusSkip")] public int ConflictStatusSkip { get; set; }
        [JsonPropertyName("CharacterNotInvolved")] public int CharacterNotInvolved { get; set; }
    }

    public class GenerationRecord
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = "";
        [JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; } = DateTime.Now;
        [JsonPropertyName("Success")] public bool Success { get; set; }
        [JsonPropertyName("TotalAttempts")] public int TotalAttempts { get; set; }
        [JsonPropertyName("RewriteCount")] public int RewriteCount { get; set; }
        [JsonPropertyName("RequiresManualIntervention")] public bool RequiresManualIntervention { get; set; }
        [JsonPropertyName("FailureStages")] public List<string> FailureStages { get; set; } = new();
        [JsonPropertyName("FailureReasons")] public List<string> FailureReasons { get; set; } = new();
        [JsonPropertyName("Attempts")] public List<AttemptRecord> Attempts { get; set; } = new();
    }

    public class AttemptRecord
    {
        [JsonPropertyName("AttemptNumber")] public int AttemptNumber { get; set; }
        [JsonPropertyName("Success")] public bool Success { get; set; }
        [JsonPropertyName("FailureType")] public string? FailureType { get; set; }
        [JsonPropertyName("FailureReasons")] public List<string> FailureReasons { get; set; } = new();
        [JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}

using System;
using TM.Framework.Common.Helpers.Id;

namespace TM.Services.Framework.AI.Monitoring;

public class ApiCallRecord
{
    [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
    [System.Text.Json.Serialization.JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; } = DateTime.Now;
    [System.Text.Json.Serialization.JsonPropertyName("ModelName")] public string ModelName { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Provider")] public string Provider { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Success")] public bool Success { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("ResponseTimeMs")] public int ResponseTimeMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("InputTokens")] public int InputTokens { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("OutputTokens")] public int OutputTokens { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("FirstTokenMs")] public int FirstTokenMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("TokensPerSecond")] public double TokensPerSecond { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("ThinkingMs")] public int ThinkingMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("ToolCallCount")] public int ToolCallCount { get; set; }
}

public class StatisticsSummary
{
    [System.Text.Json.Serialization.JsonPropertyName("TotalCalls")] public int TotalCalls { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SuccessCalls")] public int SuccessCalls { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("FailedCalls")] public int FailedCalls { get; set; }
    public double SuccessRate => TotalCalls > 0 ? (double)SuccessCalls / TotalCalls * 100 : 0;
    [System.Text.Json.Serialization.JsonPropertyName("AverageResponseTime")] public double AverageResponseTime { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("TotalInputTokens")] public int TotalInputTokens { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("TotalOutputTokens")] public int TotalOutputTokens { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("FirstCallTime")] public DateTime? FirstCallTime { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("LastCallTime")] public DateTime? LastCallTime { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("AverageFirstTokenMs")] public double AverageFirstTokenMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("AverageTokensPerSecond")] public double AverageTokensPerSecond { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("TotalThinkingMs")] public long TotalThinkingMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("TotalToolCalls")] public int TotalToolCalls { get; set; }
}

public class DailyStatistics
{
    [System.Text.Json.Serialization.JsonPropertyName("Date")] public DateTime Date { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("TotalCalls")] public int TotalCalls { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SuccessCalls")] public int SuccessCalls { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("FailedCalls")] public int FailedCalls { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("AverageResponseTime")] public double AverageResponseTime { get; set; }
}

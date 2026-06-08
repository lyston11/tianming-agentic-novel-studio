using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Models
{
    public enum PayloadType
    {
        None,
        Plan,
        GeneratedContent,
        AgentExecution,
        Error
    }

    public abstract class MessagePayload
    {
        [JsonPropertyName("Type")] public PayloadType Type { get; init; }
        [JsonPropertyName("TargetPanel")] public string? TargetPanel { get; init; }
        [JsonPropertyName("RawContent")] public string? RawContent { get; set; }
    }

    public sealed class PlanPayload : MessagePayload
    {
        public PlanPayload()
        {
            Type = PayloadType.Plan;
            TargetPanel = "ExecutionPlan";
        }

        [JsonPropertyName("Steps")] public IReadOnlyList<PlanStep> Steps { get; init; } = Array.Empty<PlanStep>();
        [JsonPropertyName("ExecutionTrace")] public IReadOnlyList<ToolCallRecord>? ExecutionTrace { get; set; }

        public bool IsExecuted => ExecutionTrace != null && ExecutionTrace.Count > 0;
    }

    public sealed class AgentPayload : MessagePayload
    {
        public AgentPayload()
        {
            Type = PayloadType.AgentExecution;
        }

        [JsonPropertyName("ToolCalls")] public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = Array.Empty<ToolCallRecord>();
    }

    public class PlanStep
    {
        [JsonPropertyName("Index")] public int Index { get; set; }
        [JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("Detail")] public string Detail { get; set; } = string.Empty;
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("ChapterNumber")] public int ChapterNumber { get; set; }
    }

    public class ToolCallRecord
    {
        [JsonPropertyName("StepIndex")] public int StepIndex { get; set; }
        [JsonPropertyName("PluginName")] public string PluginName { get; set; } = string.Empty;
        [JsonPropertyName("FunctionName")] public string FunctionName { get; set; } = string.Empty;
        [JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("Arguments")] public string Arguments { get; set; } = string.Empty;
        [JsonPropertyName("Result")] public string Result { get; set; } = string.Empty;
        [JsonPropertyName("Status")] public ToolCallStatus Status { get; set; } = ToolCallStatus.Pending;
        [JsonPropertyName("StartTime")] public DateTime StartTime { get; set; }
        [JsonPropertyName("EndTime")] public DateTime? EndTime { get; set; }
        public double? DurationSeconds => EndTime.HasValue ? (EndTime.Value - StartTime).TotalSeconds : null;
        [JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }
    }

    public enum ToolCallStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }
}

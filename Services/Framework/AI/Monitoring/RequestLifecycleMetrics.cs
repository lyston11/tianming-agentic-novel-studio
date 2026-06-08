using System;

namespace TM.Services.Framework.AI.Monitoring
{

    public sealed record RequestLifecycleMetrics
    {
        public required Guid RequestId { get; init; }

        public string? ProviderId { get; init; }

        public string? ModelId { get; init; }

        public string? Endpoint { get; init; }

        public DateTime StartTime { get; init; }

        public DateTime? FirstTokenTime { get; init; }

        public DateTime? CompleteTime { get; init; }

        public TimeSpan Duration =>
            CompleteTime.HasValue ? CompleteTime.Value - StartTime : TimeSpan.Zero;

        public TimeSpan? TimeToFirstToken =>
            FirstTokenTime.HasValue ? FirstTokenTime.Value - StartTime : (TimeSpan?)null;

        public int PromptTokens { get; init; }

        public int CompletionTokens { get; init; }

        public int TotalTokens => PromptTokens + CompletionTokens;

        public int? ThinkingTokens { get; init; }

        public int ToolCallCount { get; init; }

        public int ToolCallSuccessCount { get; init; }

        public int ToolCallFailedCount { get; init; }

        public bool ToolCircuitBroken { get; init; }

        public int RetryCount { get; init; }

        public string? FallbackReason { get; init; }

        public string? ErrorCode { get; init; }

        public string? ErrorMessage { get; init; }
    }
}

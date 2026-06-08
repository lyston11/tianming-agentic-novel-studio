using System;

namespace TM.Services.Framework.AI.SemanticKernel.Chunk
{
    public interface IStreamChunk
    {
        Guid RunId { get; }
        DateTime Timestamp { get; }
        int Sequence { get; }
    }

    public record TextDeltaChunk(string Content) : IStreamChunk
    {
        public Guid RunId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Sequence { get; init; }
    }

    public record ThinkingDeltaChunk(string Content, string? Kind = null) : IStreamChunk
    {
        public Guid RunId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Sequence { get; init; }
    }

    public record ThinkingCompleteChunk(string FullContent, int DurationMs, string? Kind = null) : IStreamChunk
    {
        public Guid RunId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Sequence { get; init; }
    }

    public record ToolCallChunk(string ToolName, string Arguments) : IStreamChunk
    {
        public Guid RunId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Sequence { get; init; }
    }

    public record ToolCallStartChunk(
        string PluginName,
        string FunctionName,
        string? Arguments = null,
        int StepIndex = 0) : IStreamChunk
    {
        public Guid RunId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Sequence { get; init; }
    }

    public record ToolCallCompletedChunk(
        string PluginName,
        string FunctionName,
        bool Succeeded,
        string? Result = null,
        string? ErrorMessage = null,
        int StepIndex = 0) : IStreamChunk
    {
        public Guid RunId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Sequence { get; init; }
    }

    public record ErrorChunk(string Category, string Message) : IStreamChunk
    {
        public Guid RunId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Sequence { get; init; }

        public string? UserFriendlyMessage { get; init; }

        public int? HttpStatusCode { get; init; }

        public Exception? InnerException { get; init; }

        public string? ProviderId { get; init; }

        public string? ModelId { get; init; }
    }

    public record UsageChunk(int PromptTokens, int CompletionTokens) : IStreamChunk
    {
        public Guid RunId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Sequence { get; init; }
    }

    public record StreamCompleteChunk(string? FinishReason = null) : IStreamChunk
    {
        public Guid RunId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Sequence { get; init; }
    }
}

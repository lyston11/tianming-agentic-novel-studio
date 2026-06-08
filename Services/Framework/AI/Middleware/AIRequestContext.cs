using System;
using System.Collections.Generic;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Core.Capabilities;
using TM.Services.Framework.AI.SemanticKernel.Chunk;

namespace TM.Services.Framework.AI.Middleware
{

    public enum MiddlewareStage
    {
        BeforeRequest = 0,

        TransformSettings = 1,

        OnChunk = 2,

        AfterResponse = 3,

        OnError = 4,
    }

    public sealed record AIRequestContext
    {
        public MiddlewareStage Stage { get; init; }

        public required Guid RunId { get; init; }

        public UserConfiguration? Config { get; init; }

        public PromptExecutionSettings? Settings { get; init; }

        public ChatHistory? ChatHistory { get; init; }

        public ResolvedCapability? Resolved { get; init; }

        public IStreamChunk? Chunk { get; init; }

        public string? FinalAnswer { get; init; }

        public Exception? Error { get; init; }

        public IDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
    }
}

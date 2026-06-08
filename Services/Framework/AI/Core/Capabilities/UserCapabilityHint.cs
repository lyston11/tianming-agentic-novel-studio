using System.Collections.Generic;

namespace TM.Services.Framework.AI.Core.Capabilities
{
    public sealed record UserCapabilityHint
    {

        public string? ReasoningEffort { get; init; }

        public bool? ThinkingEnabled { get; init; }

        public bool? CapabilitiesDetected { get; init; }

        public bool? SupportsReasoningEffort { get; init; }

        public IReadOnlyList<string>? SupportedEffortLevels { get; init; }

        public bool? SupportsThinking { get; init; }

        public bool? SupportsLongContext { get; init; }

        public bool? SupportsThinkingBudget { get; init; }

        public bool? SupportsIncludeThoughts { get; init; }

        public bool? SupportsNativeToolUse { get; init; }

        public bool? IsCompatibilityFallback { get; init; }

        public bool? IsThinkingNotSupported { get; init; }
    }
}

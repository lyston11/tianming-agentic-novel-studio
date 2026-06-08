using System;
using System.Collections.Generic;

namespace TM.Services.Framework.AI.Core.Capabilities
{

    public enum ProviderEndpointKind
    {
        Unknown = 0,

        Native = 1,

        OpenAICompat = 2,

        Aggregator = 3,
    }

    public enum ResponseThinkingMode
    {
        None = 0,

        TagBased = 1,

        Metadata = 2,

        OpenAIReasoning = 3,
    }

    public enum RequestParameterMode
    {
        None = 0,

        OpenAIReasoningEffort = 1,

        OpenRouterReasoning = 2,

        AnthropicThinking = 3,

        GoogleThinkingConfig = 4,

        QwenEnableThinking = 5,

        DoubaoEnableThinking = 6,

        EnableThinkingFlag = 7,

        DeepSeekV4Thinking = 8,
    }

    public sealed record ReasoningCapability
    {
        public bool SupportsReasoningEffort { get; init; }

        public IReadOnlyList<string> SupportedEffortLevels { get; init; } = Array.Empty<string>();

        public string? DefaultEffort { get; init; }

        public string? MaxLevel { get; init; }
    }

    public sealed record ThinkingCapability
    {
        public bool SupportsThinking { get; init; }

        public bool SupportsThinkingBudget { get; init; }

        public bool SupportsIncludeThoughts { get; init; }

        public int? DefaultBudget { get; init; }

        public int? MaxBudget { get; init; }

        public ResponseThinkingMode ResponseMode { get; init; } = ResponseThinkingMode.TagBased;

        public IReadOnlyDictionary<string, int>? BudgetByEffort { get; init; }
    }

    public sealed record ToolCapability
    {
        public bool SupportsNativeToolUse { get; init; } = true;

        public bool SupportsPromptToolUse { get; init; }
    }

    public sealed record ProviderCapabilityDescriptor
    {
        public required string ProviderId { get; init; }

        public string? DisplayName { get; init; }

        public ProviderEndpointKind EndpointKind { get; init; } = ProviderEndpointKind.OpenAICompat;

        public RequestParameterMode RequestParameterMode { get; init; } = RequestParameterMode.None;

        public ReasoningCapability Reasoning { get; init; } = new();

        public ThinkingCapability Thinking { get; init; } = new();

        public ToolCapability Tools { get; init; } = new();

        public bool IsCompatibilityFallback { get; init; }

        public bool IsThinkingNotSupported { get; init; }

        public bool IsProxyProvider { get; init; }
    }

    public sealed record ModelCapabilityDescriptor
    {
        public required string ProviderId { get; init; }

        public required string ModelId { get; init; }

        public ReasoningCapability? Reasoning { get; init; }

        public ThinkingCapability? Thinking { get; init; }

        public ToolCapability? Tools { get; init; }

        public RequestParameterMode? RequestParameterMode { get; init; }

        public bool CapabilitiesDetected { get; init; }
    }

    public sealed record ResolvedCapability
    {
        public required string ProviderId { get; init; }

        public required string ModelId { get; init; }

        public RequestParameterMode RequestParameterMode { get; init; } = RequestParameterMode.None;

        public ReasoningCapability Reasoning { get; init; } = new();

        public ThinkingCapability Thinking { get; init; } = new();

        public ToolCapability Tools { get; init; } = new();

        public bool IsCompatibilityFallback { get; init; }

        public string? ResolvedProxyTarget { get; init; }

        public int HitPriority { get; init; }

        public string? HitSource { get; init; }

        public bool HasThinkingToggle
            => RequestParameterMode != RequestParameterMode.None
               && (Reasoning.SupportsReasoningEffort || Thinking.SupportsThinking);

        public bool HasEffortLevels
        {
            get
            {
                foreach (var level in Reasoning.SupportedEffortLevels)
                {
                    if (!string.Equals(level, EffortConstants.None, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }
    }

    public static class EffortConstants
    {
        public const string Unset = "";

        public const string None = "none";

        public const string Minimal = "minimal";

        public const string Low = "low";

        public const string Medium = "medium";

        public const string High = "high";

        public const string XHigh = "xhigh";

        public const string Max = "max";

        public static readonly string[] All = { None, Minimal, Low, Medium, High, XHigh, Max };

        public static bool IsValid(string? value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(value, All[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Unset;
            var v = value.Trim().ToLowerInvariant();
            return IsValid(v) ? v : Unset;
        }
    }

    public static class StandardBudgetByEffort
    {
        public static readonly IReadOnlyDictionary<string, int> Anthropic
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [EffortConstants.Minimal] = 1024,
                [EffortConstants.Low] = 2048,
                [EffortConstants.Medium] = 4096,
                [EffortConstants.High] = 16384,
                [EffortConstants.XHigh] = 32768,
                [EffortConstants.Max] = 64000,
            };

        public static readonly IReadOnlyDictionary<string, int> Google
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [EffortConstants.Minimal] = 1024,
                [EffortConstants.Low] = 2048,
                [EffortConstants.Medium] = 8192,
                [EffortConstants.High] = 24576,
                [EffortConstants.XHigh] = 32768,
            };

        public static readonly IReadOnlyDictionary<string, int> EnableThinking
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [EffortConstants.Minimal] = 1024,
                [EffortConstants.Low] = 4096,
                [EffortConstants.Medium] = 8192,
                [EffortConstants.High] = 24576,
                [EffortConstants.XHigh] = 32768,
            };
    }

    public sealed record EffortOption(string WireValue, string DisplayLabel)
    {
        public static string FormatLabel(string? wireValue)
        {
            return (wireValue ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "" => "默认",
                EffortConstants.None => "None",
                EffortConstants.Minimal => "Minimal",
                EffortConstants.Low => "Low",
                EffortConstants.Medium => "Medium",
                EffortConstants.High => "High",
                EffortConstants.XHigh => "XHigh",
                EffortConstants.Max => "Max",
                _ => wireValue ?? string.Empty,
            };
        }

        public static IReadOnlyList<EffortOption> BuildList(IReadOnlyList<string>? levels)
        {
            if (levels == null || levels.Count == 0) return Array.Empty<EffortOption>();

            var list = new List<EffortOption>(levels.Count + 1)
            {
                new EffortOption(EffortConstants.Unset, FormatLabel(EffortConstants.Unset)),
            };
            foreach (var lv in levels)
            {
                if (string.Equals(lv, EffortConstants.None, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new EffortOption(lv, FormatLabel(lv)));
            }
            return list.Count > 1 ? list : Array.Empty<EffortOption>();
        }
    }

    public sealed record ThinkingStateOption(bool? State, string DisplayLabel)
    {
        public static readonly IReadOnlyList<ThinkingStateOption> All = new[]
        {
            new ThinkingStateOption(null,  "默认"),
            new ThinkingStateOption(true,  "启用"),
        };
    }
}

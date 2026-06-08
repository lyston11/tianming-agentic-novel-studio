using System.Collections.Generic;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.Core.Capabilities.Builders
{

    public sealed record ThinkingParameterContext
    {
        public required PromptExecutionSettings Settings { get; init; }

        public required ResolvedCapability Resolved { get; init; }

        public string EffectiveReasoningEffort { get; init; } = string.Empty;

        public int EffectiveBudget { get; init; }

        public bool IsThinkingEnabled { get; init; }

        public string ModelIdForLog { get; init; } = string.Empty;

        public IDictionary<string, object> GetOrCreateExtensionData()
        {
            Settings.ExtensionData ??= new Dictionary<string, object>();
            return Settings.ExtensionData;
        }
    }

    public interface IThinkingParameterBuilder
    {
        RequestParameterMode SupportedMode { get; }

        void Apply(ThinkingParameterContext ctx);
    }
}

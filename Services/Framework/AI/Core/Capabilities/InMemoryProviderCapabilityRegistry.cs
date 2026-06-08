using System;
using System.Collections.Generic;

namespace TM.Services.Framework.AI.Core.Capabilities
{
    public sealed class InMemoryProviderCapabilityRegistry : IProviderCapabilityRegistry
    {
        private readonly Dictionary<string, ProviderCapabilityDescriptor> _providers
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ModelCapabilityDescriptor> _models
            = new(StringComparer.OrdinalIgnoreCase);

        public ProviderCapabilityDescriptor? GetProvider(string? providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return null;
            return _providers.TryGetValue(providerId, out var d) ? d : null;
        }

        public ModelCapabilityDescriptor? GetModel(string? providerId, string? modelId)
        {
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelId)) return null;
            var key = BuildKey(providerId, modelId);
            return _models.TryGetValue(key, out var d) ? d : null;
        }

        public IReadOnlyCollection<string> GetRegisteredProviderIds() => _providers.Keys;

        public InMemoryProviderCapabilityRegistry Add(ProviderCapabilityDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            _providers[descriptor.ProviderId] = descriptor;
            return this;
        }

        public InMemoryProviderCapabilityRegistry Add(ModelCapabilityDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            var key = BuildKey(descriptor.ProviderId, descriptor.ModelId);
            _models[key] = descriptor;
            return this;
        }

        public static InMemoryProviderCapabilityRegistry CreateBuiltIn()
        {
            var r = new InMemoryProviderCapabilityRegistry();

            r.Add(new ProviderCapabilityDescriptor
            {
                ProviderId = "OpenAI",
                DisplayName = "OpenAI",
                EndpointKind = ProviderEndpointKind.Native,
                RequestParameterMode = RequestParameterMode.OpenAIReasoningEffort,
                Thinking = new ThinkingCapability
                {
                    ResponseMode = ResponseThinkingMode.OpenAIReasoning,
                },
                Tools = new ToolCapability { SupportsNativeToolUse = true },
            });

            r.Add(new ProviderCapabilityDescriptor
            {
                ProviderId = "Anthropic",
                DisplayName = "Anthropic",
                EndpointKind = ProviderEndpointKind.Native,
                RequestParameterMode = RequestParameterMode.AnthropicThinking,
                Thinking = new ThinkingCapability
                {
                    SupportsThinkingBudget = true,
                    DefaultBudget = 4096,
                    MaxBudget = 64000,
                    ResponseMode = ResponseThinkingMode.Metadata,
                    BudgetByEffort = StandardBudgetByEffort.Anthropic,
                },
                Tools = new ToolCapability { SupportsNativeToolUse = true },
            });

            r.Add(new ProviderCapabilityDescriptor
            {
                ProviderId = "Google",
                DisplayName = "Google Gemini",
                EndpointKind = ProviderEndpointKind.Native,
                RequestParameterMode = RequestParameterMode.GoogleThinkingConfig,
                Thinking = new ThinkingCapability
                {
                    SupportsThinkingBudget = true,
                    SupportsIncludeThoughts = true,
                    DefaultBudget = 8192,
                    MaxBudget = 32768,
                    ResponseMode = ResponseThinkingMode.OpenAIReasoning,
                    BudgetByEffort = StandardBudgetByEffort.Google,
                },
                Tools = new ToolCapability { SupportsNativeToolUse = true },
            });

            r.Add(new ProviderCapabilityDescriptor
            {
                ProviderId = "OpenRouter",
                DisplayName = "OpenRouter",
                EndpointKind = ProviderEndpointKind.Aggregator,
                RequestParameterMode = RequestParameterMode.OpenRouterReasoning,
                IsProxyProvider = true,
                Reasoning = new ReasoningCapability
                {
                    MaxLevel = EffortConstants.High,
                },
                Thinking = new ThinkingCapability
                {
                    ResponseMode = ResponseThinkingMode.OpenAIReasoning,
                },
                Tools = new ToolCapability { SupportsNativeToolUse = true },
            });

            r.Add(new ProviderCapabilityDescriptor
            {
                ProviderId = "DeepSeek",
                DisplayName = "DeepSeek",
                EndpointKind = ProviderEndpointKind.OpenAICompat,
                RequestParameterMode = RequestParameterMode.None,
                Thinking = new ThinkingCapability
                {
                    ResponseMode = ResponseThinkingMode.TagBased,
                },
                Tools = new ToolCapability { SupportsNativeToolUse = true },
            });

            r.Add(new ProviderCapabilityDescriptor
            {
                ProviderId = "Qwen",
                DisplayName = "Qwen / DashScope",
                EndpointKind = ProviderEndpointKind.OpenAICompat,
                RequestParameterMode = RequestParameterMode.QwenEnableThinking,
                Thinking = new ThinkingCapability
                {
                    SupportsThinkingBudget = true,
                    DefaultBudget = 8192,
                    MaxBudget = 32768,
                    ResponseMode = ResponseThinkingMode.TagBased,
                    BudgetByEffort = StandardBudgetByEffort.EnableThinking,
                },
                Tools = new ToolCapability { SupportsNativeToolUse = true },
            });

            r.Add(new ProviderCapabilityDescriptor
            {
                ProviderId = "Doubao",
                DisplayName = "Doubao（豆包）",
                EndpointKind = ProviderEndpointKind.OpenAICompat,
                RequestParameterMode = RequestParameterMode.DoubaoEnableThinking,
                Thinking = new ThinkingCapability
                {
                    ResponseMode = ResponseThinkingMode.TagBased,
                    BudgetByEffort = StandardBudgetByEffort.EnableThinking,
                },
                Tools = new ToolCapability { SupportsNativeToolUse = true },
            });

            return r;
        }

        private static string BuildKey(string providerId, string modelId)
            => $"{providerId.ToLowerInvariant()}|{modelId.ToLowerInvariant()}";
    }
}

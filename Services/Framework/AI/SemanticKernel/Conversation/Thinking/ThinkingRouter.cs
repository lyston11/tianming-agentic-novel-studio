using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.SemanticKernel;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Core.Capabilities;
using TM.Services.Framework.AI.Core.Capabilities.Builders;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking.Strategies;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking
{
    internal static class ThinkingRequestAmbientContext
    {
        private static readonly AsyncLocal<IReadOnlyDictionary<string, object>?> _extensionDataSlot = new();

        public static IReadOnlyDictionary<string, object>? CurrentExtensionData
        {
            get => _extensionDataSlot.Value;
            set => _extensionDataSlot.Value = value;
        }
    }

    public class ThinkingRouter
    {
        private readonly IThinkingStrategy _strategy;

        public ThinkingRouter(string providerType)
        {
            _strategy = providerType switch
            {
                "Anthropic" => new MetadataThinkingStrategy(),
                "Google" => new OpenAIReasoningStrategy(),
                _ => new OpenAIReasoningStrategy()
            };
        }

        public ThinkingRouteResult Route(StreamingChatMessageContent chunk)
            => _strategy.Extract(chunk);

        public ThinkingRouteResult Flush()
            => _strategy.Flush();

        public static void InjectRequestParameters(PromptExecutionSettings settings, string providerType, UserConfiguration config)
        {
            if (settings == null || config == null) return;

            ThinkingRequestAmbientContext.CurrentExtensionData = null;

            var modelId = config.ModelId ?? string.Empty;
            var providerId = config.ProviderId;
            var endpoint = config.CustomEndpoint;
            if (config.ThinkingEnabled != true)
            {
                TM.App.Log($"[ThinkingRouter] 思考=默认（不注入字段，服务端默认）: model={modelId}");
                return;
            }

            if (ChatModeSettings.IsThinkingDisabledByCap(providerId, endpoint, modelId))
            {
                TM.App.Log($"[ThinkingRouter] 思考运行时 cap 命中，强制不注入思考字段: model={modelId}");
                return;
            }

            var rawEffort = EffortConstants.Normalize(config.ReasoningEffort);
            var effortCap = ChatModeSettings.GetEffortCap(providerId, endpoint, modelId);
            var reasoningEffort = ChatModeSettings.ApplyEffortCap(rawEffort, effortCap);
            if (effortCap != null && !string.Equals(rawEffort, reasoningEffort, StringComparison.OrdinalIgnoreCase))
            {
                TM.App.Log($"[ThinkingRouter] 应用推理等级 cap: model={modelId}, 用户={ChatModeSettings.FormatEffort(rawEffort)} → 实发={ChatModeSettings.FormatEffort(reasoningEffort)}");
            }

            settings.ExtensionData ??= new Dictionary<string, object>();

            var resolvedCapability = CapabilityServices.DefaultResolver.Resolve(
                providerId: providerId,
                modelId: modelId,
                endpoint: endpoint,
                userHint: new UserCapabilityHint
                {
                    ReasoningEffort = rawEffort,
                    ThinkingEnabled = true,
                    CapabilitiesDetected = config.CapabilitiesDetected,
                    SupportsThinking = config.CapabilitiesDetected ? config.SupportsThinking : (bool?)null,
                    SupportsReasoningEffort = config.CapabilitiesDetected ? config.SupportsReasoningEffort : (bool?)null,
                    SupportedEffortLevels = config.SupportedEffortLevels?.Count > 0
                        ? config.SupportedEffortLevels
                        : null,
                });

            var maxLevel = resolvedCapability.Reasoning.MaxLevel;
            if (!string.IsNullOrEmpty(maxLevel))
            {
                var capped = ChatModeSettings.ApplyEffortCap(reasoningEffort, maxLevel);
                if (!string.Equals(reasoningEffort, capped, StringComparison.OrdinalIgnoreCase))
                {
                    TM.App.Log($"[ThinkingRouter] 应用聚合层 MaxLevel 封顶: model={modelId}, {ChatModeSettings.FormatEffort(reasoningEffort)} → {ChatModeSettings.FormatEffort(capped)}");
                    reasoningEffort = capped;
                }
            }

            bool isThinkingClassMode = resolvedCapability.RequestParameterMode is
                RequestParameterMode.AnthropicThinking
                or RequestParameterMode.GoogleThinkingConfig
                or RequestParameterMode.QwenEnableThinking
                or RequestParameterMode.DoubaoEnableThinking
                or RequestParameterMode.EnableThinkingFlag
                or RequestParameterMode.DeepSeekV4Thinking;
            if (isThinkingClassMode && !resolvedCapability.Thinking.SupportsThinking)
            {
                TM.App.Log(config.CapabilitiesDetected
                    ? $"[ThinkingRouter] 思考=启用但模型不支持（探测结果）: model={modelId}"
                    : $"[ThinkingRouter] 思考=启用但模型不支持（家族识别）: model={modelId}");
                return;
            }

            if (string.IsNullOrEmpty(reasoningEffort)
                && resolvedCapability.RequestParameterMode is RequestParameterMode.OpenRouterReasoning or RequestParameterMode.OpenAIReasoningEffort
                && resolvedCapability.Reasoning.SupportsReasoningEffort)
            {
                var defaultEffort = EffortConstants.Normalize(resolvedCapability.Reasoning.DefaultEffort);
                if (string.IsNullOrEmpty(defaultEffort)
                    || string.Equals(defaultEffort, EffortConstants.None, StringComparison.OrdinalIgnoreCase))
                {
                    defaultEffort = EffortConstants.Medium;
                    foreach (var level in resolvedCapability.Reasoning.SupportedEffortLevels)
                    {
                        if (string.Equals(level, EffortConstants.None, StringComparison.OrdinalIgnoreCase)) continue;
                        defaultEffort = level;
                        break;
                    }
                }

                var defaultReasoningEffort = ChatModeSettings.ApplyEffortCap(defaultEffort, effortCap);
                if (!string.IsNullOrEmpty(maxLevel))
                    defaultReasoningEffort = ChatModeSettings.ApplyEffortCap(defaultReasoningEffort, maxLevel);

                if (!string.IsNullOrEmpty(defaultReasoningEffort))
                {
                    reasoningEffort = defaultReasoningEffort;
                    TM.App.Log($"[ThinkingRouter] 默认 reasoning_effort={defaultReasoningEffort}（启用思考且未选强度）: model={modelId}");
                }
            }

            int budget;
            if (!string.IsNullOrEmpty(reasoningEffort)
                && resolvedCapability.Thinking.BudgetByEffort != null
                && resolvedCapability.Thinking.BudgetByEffort.TryGetValue(reasoningEffort, out var mappedBudget)
                && mappedBudget > 0)
            {
                budget = mappedBudget;
            }
            else if (resolvedCapability.Thinking.DefaultBudget is > 0)
            {
                budget = resolvedCapability.Thinking.DefaultBudget.Value;
            }
            else
            {
                budget = CalcBudget(settings);
            }

            var ctx = new ThinkingParameterContext
            {
                Settings = settings,
                Resolved = resolvedCapability,
                EffectiveReasoningEffort = reasoningEffort,
                EffectiveBudget = budget,
                IsThinkingEnabled = true,
                ModelIdForLog = modelId,
            };
            CapabilityServices.DefaultBuilders.Apply(ctx);

            if (settings.ExtensionData != null && settings.ExtensionData.Count > 0)
            {
                ThinkingRequestAmbientContext.CurrentExtensionData =
                    new Dictionary<string, object>(settings.ExtensionData);
            }
        }

        private static int CalcBudget(PromptExecutionSettings settings)
        {
            if (settings is Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings oaiSettings
                && oaiSettings.MaxTokens is > 0)
            {
                return Math.Clamp(oaiSettings.MaxTokens.Value / 2, 4000, 16000);
            }

            if (settings.ExtensionData?.TryGetValue("max_tokens", out var v) == true)
            {
                var max = v switch
                {
                    int i => i,
                    long l => (int)l,
                    double d => (int)d,
                    _ => 0
                };
                if (max > 0)
                    return Math.Clamp(max / 2, 4000, 16000);
            }
            return 8000;
        }
    }
}

using System;
using System.Collections.Generic;

namespace TM.Services.Framework.AI.Core.Capabilities
{
    public sealed class ProviderCapabilityResolver
    {
        private readonly IProviderCapabilityRegistry _registry;

        public ProviderCapabilityResolver(IProviderCapabilityRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public ResolvedCapability Resolve(
            string providerId,
            string modelId,
            string? endpoint = null,
            UserCapabilityHint? userHint = null)
        {
            providerId ??= string.Empty;
            modelId ??= string.Empty;

            var reasoning = new ReasoningCapability();
            var thinking = new ThinkingCapability();
            var tools = new ToolCapability();
            var requestMode = RequestParameterMode.None;
            int hitPriority = 0;
            string hitSource = "(none)";
            string? proxyTarget = null;
            bool isCompatibilityFallback = false;

            void TryUpdateHit(int newPriority, string newSource)
            {
                if (hitPriority == 0 || newPriority < hitPriority)
                {
                    hitPriority = newPriority;
                    hitSource = newSource;
                }
            }

            if (ModelFamilyClassifier.IsThinkingModel(modelId, providerId))
            {
                thinking = thinking with { SupportsThinking = true };
                TryUpdateHit(7, "ModelFamilyClassifier:IsThinkingModel");
            }
            if (ModelFamilyClassifier.IsReasoningEffortModel(modelId, providerId))
            {
                reasoning = reasoning with { SupportsReasoningEffort = true };
                TryUpdateHit(7, "ModelFamilyClassifier:IsReasoningEffortModel");
            }
            if (ModelFamilyClassifier.IsOpenRouterReasoningModel(modelId, providerId)
                && (endpoint?.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                reasoning = reasoning with { SupportsReasoningEffort = true };
                requestMode = RequestParameterMode.OpenRouterReasoning;
                TryUpdateHit(7, "ModelFamilyClassifier:IsOpenRouterReasoningModel");
            }

            var familyLevels = ModelFamilyClassifier.GetSupportedEffortLevels(modelId, providerId);
            if (familyLevels.Count > 0)
            {
                reasoning = reasoning with
                {
                    SupportedEffortLevels = familyLevels,
                    DefaultEffort = ModelFamilyClassifier.GetDefaultEffort(modelId, providerId) ?? reasoning.DefaultEffort,
                };
                TryUpdateHit(7, "ModelFamilyClassifier:GetSupportedEffortLevels");
            }

            var providerDesc = _registry.GetProvider(providerId);
            if (providerDesc != null)
            {
                reasoning = reasoning with
                {
                    SupportsReasoningEffort = providerDesc.Reasoning.SupportsReasoningEffort
                        || reasoning.SupportsReasoningEffort,
                    SupportedEffortLevels = providerDesc.Reasoning.SupportedEffortLevels.Count > 0
                        ? providerDesc.Reasoning.SupportedEffortLevels
                        : reasoning.SupportedEffortLevels,
                    DefaultEffort = providerDesc.Reasoning.DefaultEffort ?? reasoning.DefaultEffort,
                    MaxLevel = providerDesc.Reasoning.MaxLevel ?? reasoning.MaxLevel,
                };
                thinking = thinking with
                {
                    SupportsThinking = providerDesc.Thinking.SupportsThinking
                        || thinking.SupportsThinking,
                    SupportsThinkingBudget = providerDesc.Thinking.SupportsThinkingBudget
                        || thinking.SupportsThinkingBudget,
                    SupportsIncludeThoughts = providerDesc.Thinking.SupportsIncludeThoughts
                        || thinking.SupportsIncludeThoughts,
                    DefaultBudget = providerDesc.Thinking.DefaultBudget ?? thinking.DefaultBudget,
                    MaxBudget = providerDesc.Thinking.MaxBudget ?? thinking.MaxBudget,
                    ResponseMode = providerDesc.Thinking.ResponseMode != ResponseThinkingMode.None
                        ? providerDesc.Thinking.ResponseMode
                        : thinking.ResponseMode,
                    BudgetByEffort = providerDesc.Thinking.BudgetByEffort?.Count > 0
                        ? providerDesc.Thinking.BudgetByEffort
                        : thinking.BudgetByEffort,
                };
                tools = providerDesc.Tools;
                requestMode = providerDesc.RequestParameterMode;
                isCompatibilityFallback = providerDesc.IsCompatibilityFallback;
                TryUpdateHit(6, $"ProviderDescriptor:{providerId}");

                if (providerDesc.IsThinkingNotSupported)
                {
                    thinking = thinking with { SupportsThinking = false };
                }
            }

            ProxyProviderHint? proxyHint = null;
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                proxyHint = ProxyProviderResolver.Resolve(endpoint, modelId);
                if (proxyHint != null)
                {
                    proxyTarget = $"{proxyHint.ProxyDomain} → {proxyHint.UnderlyingProviderId}";
                    var underlyingDesc = _registry.GetProvider(proxyHint.UnderlyingProviderId);
                    if (underlyingDesc != null)
                    {
                        reasoning = reasoning with
                        {
                            SupportsReasoningEffort = underlyingDesc.Reasoning.SupportsReasoningEffort
                                || reasoning.SupportsReasoningEffort,
                            SupportedEffortLevels = underlyingDesc.Reasoning.SupportedEffortLevels.Count > 0
                                ? underlyingDesc.Reasoning.SupportedEffortLevels
                                : reasoning.SupportedEffortLevels,
                            DefaultEffort = underlyingDesc.Reasoning.DefaultEffort ?? reasoning.DefaultEffort,
                            MaxLevel = underlyingDesc.Reasoning.MaxLevel ?? reasoning.MaxLevel,
                        };
                        thinking = thinking with
                        {
                            SupportsThinking = underlyingDesc.Thinking.SupportsThinking
                                || thinking.SupportsThinking,
                            SupportsThinkingBudget = underlyingDesc.Thinking.SupportsThinkingBudget
                                || thinking.SupportsThinkingBudget,
                            SupportsIncludeThoughts = underlyingDesc.Thinking.SupportsIncludeThoughts
                                || thinking.SupportsIncludeThoughts,
                            DefaultBudget = underlyingDesc.Thinking.DefaultBudget ?? thinking.DefaultBudget,
                            MaxBudget = underlyingDesc.Thinking.MaxBudget ?? thinking.MaxBudget,
                            ResponseMode = proxyHint.UnderlyingKind == ProviderEndpointKind.OpenAICompat
                                ? ResponseThinkingMode.TagBased
                                : underlyingDesc.Thinking.ResponseMode,
                            BudgetByEffort = underlyingDesc.Thinking.BudgetByEffort?.Count > 0
                                ? underlyingDesc.Thinking.BudgetByEffort
                                : thinking.BudgetByEffort,
                        };
                        tools = underlyingDesc.Tools;
                        TryUpdateHit(3, $"Proxy:{proxyHint.ProxyDomain}/{proxyHint.UnderlyingProviderId}");
                    }
                }
            }

            var modelDesc = _registry.GetModel(providerId, modelId);
            if (modelDesc != null)
            {
                if (modelDesc.Reasoning != null)
                {
                    var mr = modelDesc.Reasoning;
                    reasoning = reasoning with
                    {
                        SupportsReasoningEffort = mr.SupportsReasoningEffort || reasoning.SupportsReasoningEffort,
                        SupportedEffortLevels = mr.SupportedEffortLevels.Count > 0
                            ? mr.SupportedEffortLevels
                            : reasoning.SupportedEffortLevels,
                        DefaultEffort = mr.DefaultEffort ?? reasoning.DefaultEffort,
                        MaxLevel = mr.MaxLevel ?? reasoning.MaxLevel,
                    };
                }
                if (modelDesc.Thinking != null)
                {
                    var mt = modelDesc.Thinking;
                    thinking = thinking with
                    {
                        SupportsThinking = mt.SupportsThinking || thinking.SupportsThinking,
                        SupportsThinkingBudget = mt.SupportsThinkingBudget || thinking.SupportsThinkingBudget,
                        SupportsIncludeThoughts = mt.SupportsIncludeThoughts || thinking.SupportsIncludeThoughts,
                        DefaultBudget = mt.DefaultBudget ?? thinking.DefaultBudget,
                        MaxBudget = mt.MaxBudget ?? thinking.MaxBudget,
                        ResponseMode = mt.ResponseMode != ResponseThinkingMode.None
                            ? mt.ResponseMode
                            : thinking.ResponseMode,
                        BudgetByEffort = mt.BudgetByEffort?.Count > 0
                            ? mt.BudgetByEffort
                            : thinking.BudgetByEffort,
                    };
                }
                if (modelDesc.Tools != null) tools = modelDesc.Tools;
                if (modelDesc.RequestParameterMode.HasValue)
                    requestMode = modelDesc.RequestParameterMode.Value;
                if (modelDesc.CapabilitiesDetected)
                {
                    TryUpdateHit(5, $"ModelDescriptor(detected):{providerId}/{modelId}");
                }
                else
                {
                    TryUpdateHit(2, $"ModelDescriptor(pattern):{providerId}/{modelId}");
                }
            }

            var inferredMode = ModelFamilyClassifier.GetRequestParameterMode(modelId, providerId, endpoint);
            if (inferredMode != RequestParameterMode.None)
            {
                requestMode = inferredMode;

                bool inferredSupportsBudget = inferredMode is
                    RequestParameterMode.AnthropicThinking
                    or RequestParameterMode.GoogleThinkingConfig
                    or RequestParameterMode.QwenEnableThinking
                    or RequestParameterMode.EnableThinkingFlag;

                if (inferredSupportsBudget)
                {
                    thinking = thinking with { SupportsThinkingBudget = true };
                }

                TryUpdateHit(2, $"ModelFamilyClassifier.GetRequestParameterMode={inferredMode}");
            }

            if (userHint?.CapabilitiesDetected == true)
            {
                if (userHint.SupportsReasoningEffort.HasValue)
                    reasoning = reasoning with { SupportsReasoningEffort = userHint.SupportsReasoningEffort.Value };
                if (userHint.SupportedEffortLevels?.Count > 0)
                    reasoning = reasoning with { SupportedEffortLevels = userHint.SupportedEffortLevels };
                if (userHint.SupportsThinking.HasValue)
                    thinking = thinking with { SupportsThinking = userHint.SupportsThinking.Value };
                if (userHint.SupportsThinkingBudget.HasValue)
                    thinking = thinking with { SupportsThinkingBudget = userHint.SupportsThinkingBudget.Value };
                if (userHint.SupportsIncludeThoughts.HasValue)
                    thinking = thinking with { SupportsIncludeThoughts = userHint.SupportsIncludeThoughts.Value };
                if (userHint.SupportsNativeToolUse.HasValue)
                    tools = tools with { SupportsNativeToolUse = userHint.SupportsNativeToolUse.Value };

                TryUpdateHit(4, $"UserHint(detected):{providerId}/{modelId}");
            }

            if (userHint != null)
            {
                if (userHint.IsCompatibilityFallback == true)
                {
                    isCompatibilityFallback = true;
                    tools = tools with { SupportsNativeToolUse = false };
                }

                if (userHint.IsThinkingNotSupported == true)
                {
                    thinking = thinking with { SupportsThinking = false };
                    requestMode = RequestParameterMode.None;
                }

                if (userHint.ReasoningEffort != null
                    || userHint.IsCompatibilityFallback == true
                    || userHint.IsThinkingNotSupported == true)
                {
                    TryUpdateHit(1, $"UserConfig:{providerId}/{modelId}");
                }
            }

            if (reasoning.SupportsReasoningEffort && reasoning.SupportedEffortLevels.Count == 0)
            {
                var fallback = GetFallbackLevelsByMode(requestMode);
                if (fallback.Count > 0)
                {
                    reasoning = reasoning with { SupportedEffortLevels = fallback };
                }
            }

            if (!string.IsNullOrEmpty(reasoning.MaxLevel) && reasoning.SupportedEffortLevels.Count > 0)
            {
                var capIdx = GetEffortLevelIndex(reasoning.MaxLevel);
                if (capIdx >= 0)
                {
                    var filtered = new List<string>(reasoning.SupportedEffortLevels.Count);
                    foreach (var lv in reasoning.SupportedEffortLevels)
                    {
                        var idx = GetEffortLevelIndex(lv);
                        if (idx >= 0 && idx <= capIdx) filtered.Add(lv);
                    }
                    if (filtered.Count > 0 && filtered.Count != reasoning.SupportedEffortLevels.Count)
                    {
                        reasoning = reasoning with { SupportedEffortLevels = filtered };
                    }
                }
            }

            return new ResolvedCapability
            {
                ProviderId = providerId,
                ModelId = modelId,
                RequestParameterMode = requestMode,
                Reasoning = reasoning,
                Thinking = thinking,
                Tools = tools,
                IsCompatibilityFallback = isCompatibilityFallback,
                ResolvedProxyTarget = proxyTarget,
                HitPriority = hitPriority == 0 ? 7 : hitPriority,
                HitSource = hitSource,
            };
        }

        private static int GetEffortLevelIndex(string? effort)
        {
            if (string.IsNullOrEmpty(effort)) return -1;
            var v = effort.Trim().ToLowerInvariant();
            for (int i = 0; i < EffortConstants.All.Length; i++)
            {
                if (string.Equals(EffortConstants.All[i], v, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static IReadOnlyList<string> GetFallbackLevelsByMode(RequestParameterMode mode)
        {
            return mode switch
            {
                RequestParameterMode.OpenAIReasoningEffort
                  or RequestParameterMode.OpenRouterReasoning
                  or RequestParameterMode.AnthropicThinking
                  or RequestParameterMode.GoogleThinkingConfig
                  or RequestParameterMode.QwenEnableThinking
                  or RequestParameterMode.DoubaoEnableThinking
                  or RequestParameterMode.EnableThinkingFlag
                  or RequestParameterMode.DeepSeekV4Thinking
                    => new[] { EffortConstants.Low, EffortConstants.Medium, EffortConstants.High },
                _ => Array.Empty<string>(),
            };
        }
    }
}

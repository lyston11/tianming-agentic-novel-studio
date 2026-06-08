using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace TM.Services.Framework.AI.Core.Capabilities.Builders
{

    public sealed class NoneParameterBuilder : IThinkingParameterBuilder
    {
        public RequestParameterMode SupportedMode => RequestParameterMode.None;
        public void Apply(ThinkingParameterContext ctx) { }
    }

    public sealed class OpenAIReasoningEffortBuilder : IThinkingParameterBuilder
    {
        public RequestParameterMode SupportedMode => RequestParameterMode.OpenAIReasoningEffort;

        public void Apply(ThinkingParameterContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.EffectiveReasoningEffort)) return;

            if (ctx.Resolved.Reasoning.SupportedEffortLevels.Count > 0
                && !ctx.Resolved.Reasoning.SupportedEffortLevels.Contains(
                    ctx.EffectiveReasoningEffort, StringComparer.OrdinalIgnoreCase))
            {
                TM.App.Log($"[ThinkingRouter] 模型不支持 reasoning_effort={ctx.EffectiveReasoningEffort} 档位，跳过注入: model={ctx.ModelIdForLog}");
                return;
            }

            if (ctx.Settings is OpenAIPromptExecutionSettings openAi)
            {
                openAi.ReasoningEffort = ctx.EffectiveReasoningEffort;
                TM.App.Log($"[ThinkingRouter] 注入 OpenAI reasoning_effort={ctx.EffectiveReasoningEffort}, model={ctx.ModelIdForLog}");
                return;
            }

            ctx.GetOrCreateExtensionData()["reasoning_effort"] = ctx.EffectiveReasoningEffort;
            TM.App.Log($"[ThinkingRouter] 注入 reasoning_effort={ctx.EffectiveReasoningEffort}, model={ctx.ModelIdForLog}");
        }
    }

    public sealed class OpenRouterReasoningBuilder : IThinkingParameterBuilder
    {
        public RequestParameterMode SupportedMode => RequestParameterMode.OpenRouterReasoning;

        public void Apply(ThinkingParameterContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.EffectiveReasoningEffort)) return;

            if (ctx.Resolved.Reasoning.SupportedEffortLevels.Count > 0
                && !ctx.Resolved.Reasoning.SupportedEffortLevels.Contains(
                    ctx.EffectiveReasoningEffort, StringComparer.OrdinalIgnoreCase))
            {
                TM.App.Log($"[ThinkingRouter] 模型不支持 OpenRouter reasoning={ctx.EffectiveReasoningEffort} 档位，跳过注入: model={ctx.ModelIdForLog}");
                return;
            }

            ctx.GetOrCreateExtensionData()["reasoning"] = ctx.EffectiveReasoningEffort;
            TM.App.Log($"[ThinkingRouter] 注入 OpenRouter reasoning={ctx.EffectiveReasoningEffort}, model={ctx.ModelIdForLog}");
        }
    }

    public sealed class AnthropicThinkingBuilder : IThinkingParameterBuilder
    {
        public RequestParameterMode SupportedMode => RequestParameterMode.AnthropicThinking;

        public void Apply(ThinkingParameterContext ctx)
        {
            if (!ctx.IsThinkingEnabled) return;

            ctx.GetOrCreateExtensionData()["thinking"] = new Dictionary<string, object>
            {
                { "type", "enabled" },
                { "budget_tokens", ctx.EffectiveBudget },
            };
            TM.App.Log($"[ThinkingRouter] 注入 Anthropic thinking: budget={ctx.EffectiveBudget}, model={ctx.ModelIdForLog}");
        }
    }

    public sealed class GoogleThinkingConfigBuilder : IThinkingParameterBuilder
    {
        public RequestParameterMode SupportedMode => RequestParameterMode.GoogleThinkingConfig;

        public void Apply(ThinkingParameterContext ctx)
        {
            if (!ctx.IsThinkingEnabled) return;

            ctx.GetOrCreateExtensionData()["thinkingConfig"] = new Dictionary<string, object>
            {
                { "thinkingBudget", ctx.EffectiveBudget },
                { "includeThoughts", true },
            };
            TM.App.Log($"[ThinkingRouter] 注入 Gemini thinkingConfig: budget={ctx.EffectiveBudget}, includeThoughts=true, model={ctx.ModelIdForLog}");
        }
    }

    public sealed class EnableThinkingFlagBuilder : IThinkingParameterBuilder
    {
        private readonly RequestParameterMode _mode;

        public EnableThinkingFlagBuilder(RequestParameterMode mode = RequestParameterMode.EnableThinkingFlag)
        {
            _mode = mode;
        }

        public RequestParameterMode SupportedMode => _mode;

        public void Apply(ThinkingParameterContext ctx)
        {
            if (!ctx.IsThinkingEnabled) return;

            var ext = ctx.GetOrCreateExtensionData();
            ext["enable_thinking"] = true;

            if (ctx.Resolved.Thinking.SupportsThinkingBudget && ctx.EffectiveBudget > 0)
            {
                ext["thinking_budget"] = ctx.EffectiveBudget;
            }

            TM.App.Log($"[ThinkingRouter] 注入 {_mode}: enable_thinking=true{(ctx.EffectiveBudget > 0 && ctx.Resolved.Thinking.SupportsThinkingBudget ? $", thinking_budget={ctx.EffectiveBudget}" : string.Empty)}, model={ctx.ModelIdForLog}");
        }
    }

    public sealed class DeepSeekV4ThinkingBuilder : IThinkingParameterBuilder
    {
        public RequestParameterMode SupportedMode => RequestParameterMode.DeepSeekV4Thinking;

        public void Apply(ThinkingParameterContext ctx)
        {
            if (!ctx.IsThinkingEnabled) return;

            var ext = ctx.GetOrCreateExtensionData();
            ext["thinking"] = new Dictionary<string, object>
            {
                { "type", "enabled" },
            };

            string? appendedEffort = null;
            if (!string.IsNullOrEmpty(ctx.EffectiveReasoningEffort))
            {
                ext["reasoning_effort"] = ctx.EffectiveReasoningEffort;
                appendedEffort = ctx.EffectiveReasoningEffort;
            }

            TM.App.Log($"[ThinkingRouter] 注入 thinking.type=enabled{(appendedEffort != null ? $", reasoning_effort={appendedEffort}" : string.Empty)}: model={ctx.ModelIdForLog}");
        }
    }
}

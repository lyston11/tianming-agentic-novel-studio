using System;
using System.Collections.Generic;

namespace TM.Services.Framework.AI.Core.Capabilities.Builders
{
    public sealed class ThinkingParameterBuilderRegistry
    {
        private readonly Dictionary<RequestParameterMode, IThinkingParameterBuilder> _builders
            = new();

        public ThinkingParameterBuilderRegistry Register(IThinkingParameterBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            _builders[builder.SupportedMode] = builder;
            return this;
        }

        public void Apply(ThinkingParameterContext ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            if (_builders.TryGetValue(ctx.Resolved.RequestParameterMode, out var builder))
            {
                builder.Apply(ctx);
            }
        }

        public bool HasBuilder(RequestParameterMode mode) => _builders.ContainsKey(mode);

        public IReadOnlyCollection<RequestParameterMode> RegisteredModes => _builders.Keys;

        public static ThinkingParameterBuilderRegistry CreateBuiltIn()
        {
            return new ThinkingParameterBuilderRegistry()
                .Register(new NoneParameterBuilder())
                .Register(new OpenAIReasoningEffortBuilder())
                .Register(new OpenRouterReasoningBuilder())
                .Register(new AnthropicThinkingBuilder())
                .Register(new GoogleThinkingConfigBuilder())
                .Register(new EnableThinkingFlagBuilder(RequestParameterMode.EnableThinkingFlag))
                .Register(new EnableThinkingFlagBuilder(RequestParameterMode.QwenEnableThinking))
                .Register(new EnableThinkingFlagBuilder(RequestParameterMode.DoubaoEnableThinking))
                .Register(new DeepSeekV4ThinkingBuilder());
        }
    }
}

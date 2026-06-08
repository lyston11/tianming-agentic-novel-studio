using System;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking;

namespace TM.Services.Framework.AI.Middleware.Builtins
{
    public sealed class ThinkingRequestMiddleware : AIRequestMiddleware
    {
        private const string DefaultProviderType = "TagBased";

        public const string ProviderTypeKey = "_ThinkingRequest.ProviderType";

        public const string SuppressedKey = "_ThinkingRequest.Suppressed";

        public override Task TransformSettingsAsync(AIRequestContext ctx, CancellationToken ct)
        {
            if (ctx.Settings == null || ctx.Config == null) return Task.CompletedTask;

            if (ctx.Metadata.TryGetValue(SuppressedKey, out var sv) && sv is bool suppressed && suppressed)
            {
                return Task.CompletedTask;
            }

            var providerType = ResolveProviderType(ctx);

            try
            {
                ThinkingRouter.InjectRequestParameters(ctx.Settings, providerType, ctx.Config);
                ChatModeSettings.InjectLongContextParameters(ctx.Settings, ctx.Config);
                ChatModeSettings.StripUnsupportedParams(
                    ctx.Settings,
                    ctx.Config.ProviderId,
                    ctx.Config.CustomEndpoint,
                    ctx.Config.ModelId);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThinkingRequestMiddleware] 注入思考参数失败（非致命）: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private static string ResolveProviderType(AIRequestContext ctx)
        {
            if (ctx.Metadata.TryGetValue(ProviderTypeKey, out var v) && v is string s && !string.IsNullOrEmpty(s))
                return s;

            var providerId = ctx.Resolved?.ProviderId ?? ctx.Config?.ProviderId ?? string.Empty;
            if (providerId.Contains("Anthropic", System.StringComparison.OrdinalIgnoreCase)) return "Anthropic";
            if (providerId.Contains("Google", System.StringComparison.OrdinalIgnoreCase)) return "Google";

            return DefaultProviderType;
        }
    }
}

using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.SemanticKernel.Chunk;

namespace TM.Services.Framework.AI.Middleware.Builtins
{
    public sealed class UsageMetricsMiddleware : AIRequestMiddleware
    {
        public const string PromptTokensKey = "_UsageMetrics.PromptTokens";
        public const string CompletionTokensKey = "_UsageMetrics.CompletionTokens";

        public override Task OnChunkAsync(AIRequestContext ctx, CancellationToken ct)
        {
            if (ctx.Chunk is not UsageChunk usage) return Task.CompletedTask;

            UpdateMax(ctx, PromptTokensKey, usage.PromptTokens);
            UpdateMax(ctx, CompletionTokensKey, usage.CompletionTokens);

            return Task.CompletedTask;
        }

        public override Task AfterResponseAsync(AIRequestContext ctx, CancellationToken ct)
        {
            var prompt = GetInt(ctx, PromptTokensKey);
            var completion = GetInt(ctx, CompletionTokensKey);
            if (prompt == 0 && completion == 0) return Task.CompletedTask;

            TM.App.Log($"[UsageMetrics] runId={ctx.RunId}, promptTokens={prompt}, completionTokens={completion}, total={prompt + completion}");
            return Task.CompletedTask;
        }

        private static void UpdateMax(AIRequestContext ctx, string key, int value)
        {
            var current = GetInt(ctx, key);
            if (value > current) ctx.Metadata[key] = value;
        }

        private static int GetInt(AIRequestContext ctx, string key)
            => ctx.Metadata.TryGetValue(key, out var v) && v is int i ? i : 0;
    }
}

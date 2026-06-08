using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.SemanticKernel.Chunk;

namespace TM.Services.Framework.AI.Middleware.Builtins
{
    public sealed class ReasoningExtractionMiddleware : AIRequestMiddleware
    {
        public const string FullContentKey = "_Reasoning.FullContent";
        public const string DurationMsKey = "_Reasoning.DurationMs";
        public const string KindKey = "_Reasoning.Kind";

        public override Task OnChunkAsync(AIRequestContext ctx, CancellationToken ct)
        {
            if (ctx.Chunk is ThinkingCompleteChunk done)
            {
                ctx.Metadata[FullContentKey] = done.FullContent;
                ctx.Metadata[DurationMsKey] = done.DurationMs;
                if (!string.IsNullOrEmpty(done.Kind))
                {
                    ctx.Metadata[KindKey] = done.Kind;
                }
            }
            return Task.CompletedTask;
        }
    }
}

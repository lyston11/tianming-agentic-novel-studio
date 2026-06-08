using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.Middleware
{
    public sealed class AIRequestPipeline
    {
        private readonly IReadOnlyList<IAIRequestMiddleware> _middlewares;

        public AIRequestPipeline(IEnumerable<IAIRequestMiddleware> middlewares)
        {
            ArgumentNullException.ThrowIfNull(middlewares);
            _middlewares = middlewares.ToList();
        }

        public int Count => _middlewares.Count;

        public async Task RunStageAsync(AIRequestContext ctx, MiddlewareStage stage, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(ctx);

            var stagedCtx = ctx with { Stage = stage };

            foreach (var mw in _middlewares)
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    Task task = stage switch
                    {
                        MiddlewareStage.BeforeRequest => mw.BeforeRequestAsync(stagedCtx, ct),
                        MiddlewareStage.TransformSettings => mw.TransformSettingsAsync(stagedCtx, ct),
                        MiddlewareStage.OnChunk => mw.OnChunkAsync(stagedCtx, ct),
                        MiddlewareStage.AfterResponse => mw.AfterResponseAsync(stagedCtx, ct),
                        MiddlewareStage.OnError => mw.OnErrorAsync(stagedCtx, ct),
                        _ => Task.CompletedTask,
                    };
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    var name = mw.GetType().Name;
                    TM.App.Log($"[AIRequestPipeline] {stage} middleware {name} 异常: {ex.Message}");
                }
            }
        }
    }
}

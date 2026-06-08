using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Core.Errors;
using TM.Services.Framework.AI.Monitoring;

namespace TM.Services.Framework.AI.Middleware.Builtins
{
    public sealed class TraceMiddleware : AIRequestMiddleware
    {
        private const string StartKey = "_TraceMiddleware.Start";

        public override Task BeforeRequestAsync(AIRequestContext ctx, CancellationToken ct)
        {
            ctx.Metadata[StartKey] = Stopwatch.GetTimestamp();
            TM.App.Log($"[Trace] RequestStart: runId={ctx.RunId}, provider={ctx.Config?.ProviderId}, model={ctx.Config?.ModelId}");

            RequestLifecycleCollector.Track(
                ctx.RunId,
                providerId: ctx.Config?.ProviderId,
                modelId: ctx.Config?.ModelId,
                endpoint: ctx.Config?.CustomEndpoint);

            return Task.CompletedTask;
        }

        public override Task AfterResponseAsync(AIRequestContext ctx, CancellationToken ct)
        {
            var elapsed = GetElapsedMs(ctx);
            var ansLen = ctx.FinalAnswer?.Length ?? 0;
            TM.App.Log($"[Trace] RequestComplete: runId={ctx.RunId}, durationMs={elapsed}, answerLen={ansLen}");

            RequestLifecycleCollector.MarkComplete(ctx.RunId, success: true);

            return Task.CompletedTask;
        }

        public override Task OnErrorAsync(AIRequestContext ctx, CancellationToken ct)
        {
            var elapsed = GetElapsedMs(ctx);

            AIRequestError? normalized = null;
            if (ctx.Metadata.TryGetValue(ErrorNormalizeMiddleware.NormalizedErrorKey, out var raw)
                && raw is AIRequestError mErr)
            {
                normalized = mErr;
            }

            var kindStr = normalized?.Kind.ToString() ?? "Unknown";
            var msg = normalized?.Message ?? ctx.Error?.Message ?? string.Empty;

            bool isCancellation = ctx.Error is OperationCanceledException;
            if (isCancellation)
            {
                TM.App.Log($"[Trace] RequestCancelled: runId={ctx.RunId}, kind={kindStr}, durationMs={elapsed}, reason={msg}");
            }
            else
            {
                TM.App.Log($"[Trace] RequestError: runId={ctx.RunId}, kind={kindStr}, durationMs={elapsed}, msg={msg}");
            }

            RequestLifecycleCollector.MarkComplete(ctx.RunId, success: false, errorMessage: msg);

            return Task.CompletedTask;
        }

        private static long GetElapsedMs(AIRequestContext ctx)
        {
            if (ctx.Metadata.TryGetValue(StartKey, out var v) && v is long ts)
            {
                var freq = Stopwatch.Frequency;
                return (Stopwatch.GetTimestamp() - ts) * 1000L / freq;
            }
            return 0;
        }
    }
}

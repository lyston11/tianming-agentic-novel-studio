using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.Middleware.Builtins
{
    public sealed class RetryFallbackMiddleware : AIRequestMiddleware
    {
        public const string RetryCountKey = "_Retry.Count";
        public const string FallbackReasonKey = "_Retry.FallbackReason";

        public static void MarkRetry(AIRequestContext ctx, string? reason = null)
        {
            if (ctx == null) return;
            var current = ctx.Metadata.TryGetValue(RetryCountKey, out var v) && v is int rc ? rc : 0;
            ctx.Metadata[RetryCountKey] = current + 1;
            if (!string.IsNullOrEmpty(reason))
            {
                ctx.Metadata[FallbackReasonKey] = reason;
            }
            TM.Services.Framework.AI.Monitoring.RequestLifecycleCollector.ReportFallback(
                ctx.RunId, fallbackReason: reason, retryCountDelta: 1);
        }

        public static void MarkFallback(AIRequestContext ctx, string reason)
        {
            if (ctx == null || string.IsNullOrEmpty(reason)) return;
            ctx.Metadata[FallbackReasonKey] = reason;
            TM.Services.Framework.AI.Monitoring.RequestLifecycleCollector.ReportFallback(
                ctx.RunId, fallbackReason: reason);
        }

        public override Task AfterResponseAsync(AIRequestContext ctx, CancellationToken ct)
        {
            EmitSummary(ctx, "RequestComplete");
            return Task.CompletedTask;
        }

        public override Task OnErrorAsync(AIRequestContext ctx, CancellationToken ct)
        {
            EmitSummary(ctx, "RequestError");
            return Task.CompletedTask;
        }

        private static void EmitSummary(AIRequestContext ctx, string phase)
        {
            var retryCount = ctx.Metadata.TryGetValue(RetryCountKey, out var rv) && rv is int rc ? rc : 0;
            var fallbackReason = ctx.Metadata.TryGetValue(FallbackReasonKey, out var fv) && fv is string fr ? fr : null;

            if (retryCount > 0 || !string.IsNullOrEmpty(fallbackReason))
            {
                TM.App.Log($"[RetryFallback] phase={phase}, runId={ctx.RunId}, retryCount={retryCount}, fallbackReason={fallbackReason ?? "(none)"}");
            }
        }
    }
}

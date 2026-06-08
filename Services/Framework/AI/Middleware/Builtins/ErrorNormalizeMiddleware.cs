using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Core.Errors;

namespace TM.Services.Framework.AI.Middleware.Builtins
{
    public sealed class ErrorNormalizeMiddleware : AIRequestMiddleware
    {
        public const string NormalizedErrorKey = "_ErrorNormalizeMiddleware.NormalizedError";

        public override Task OnErrorAsync(AIRequestContext ctx, CancellationToken ct)
        {
            if (ctx.Error == null) return Task.CompletedTask;

            var error = ErrorChunkBridge.PublishToBus(
                ex: ctx.Error,
                providerId: ctx.Config?.ProviderId,
                modelId: ctx.Config?.ModelId,
                endpoint: ctx.Config?.CustomEndpoint,
                cancellationToken: ct,
                runId: ctx.RunId);

            ctx.Metadata[NormalizedErrorKey] = error;

            return Task.CompletedTask;
        }
    }
}

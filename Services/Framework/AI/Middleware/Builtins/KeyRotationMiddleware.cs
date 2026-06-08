using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Core;

namespace TM.Services.Framework.AI.Middleware.Builtins
{
    public sealed class KeyRotationMiddleware : AIRequestMiddleware
    {
        public const string PoolStatusKey = "_KeyRotation.PoolStatus";

        public override Task BeforeRequestAsync(AIRequestContext ctx, CancellationToken ct)
        {
            if (ctx.Config == null) return Task.CompletedTask;

            try
            {
                var rotation = ServiceLocator.Get<ApiKeyRotationService>();
                var status = rotation.GetPoolStatus(ctx.Config.ProviderId);
                if (status != null)
                {
                    ctx.Metadata[PoolStatusKey] = status;
                }
            }
            catch
            {
            }

            return Task.CompletedTask;
        }
    }
}

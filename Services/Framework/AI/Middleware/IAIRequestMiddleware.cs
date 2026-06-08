using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.Middleware
{
    public interface IAIRequestMiddleware
    {
        Task BeforeRequestAsync(AIRequestContext ctx, CancellationToken ct);

        Task TransformSettingsAsync(AIRequestContext ctx, CancellationToken ct);

        Task OnChunkAsync(AIRequestContext ctx, CancellationToken ct);

        Task AfterResponseAsync(AIRequestContext ctx, CancellationToken ct);

        Task OnErrorAsync(AIRequestContext ctx, CancellationToken ct);
    }

    public abstract class AIRequestMiddleware : IAIRequestMiddleware
    {
        public virtual Task BeforeRequestAsync(AIRequestContext ctx, CancellationToken ct) => Task.CompletedTask;
        public virtual Task TransformSettingsAsync(AIRequestContext ctx, CancellationToken ct) => Task.CompletedTask;
        public virtual Task OnChunkAsync(AIRequestContext ctx, CancellationToken ct) => Task.CompletedTask;
        public virtual Task AfterResponseAsync(AIRequestContext ctx, CancellationToken ct) => Task.CompletedTask;
        public virtual Task OnErrorAsync(AIRequestContext ctx, CancellationToken ct) => Task.CompletedTask;
    }
}

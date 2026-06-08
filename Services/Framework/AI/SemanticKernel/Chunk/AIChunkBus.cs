using System;

namespace TM.Services.Framework.AI.SemanticKernel.Chunk
{
    public static class AIChunkBus
    {
        public static event Action<IStreamChunk>? Published;

        public static void Publish(IStreamChunk? chunk)
        {
            if (chunk == null) return;
            var snapshot = Published;
            if (snapshot == null) return;
            foreach (var handler in snapshot.GetInvocationList())
            {
                try
                {
                    ((Action<IStreamChunk>)handler).Invoke(chunk);
                }
                catch (Exception ex)
                {
                    try
                    {
                        TM.App.Log($"[AIChunkBus] 订阅者抛异常已隔离: {ex.GetType().Name}: {ex.Message}");
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}

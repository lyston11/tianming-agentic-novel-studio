using System;

namespace TM.Services.Framework.AI.SemanticKernel.Chunk
{
    public static class ExecutionEventChunkAutoBridge
    {
        private static readonly object _initLock = new();
        private static bool _initialized;
        private static Action<ExecutionEvent>? _handler;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;

                _handler = OnExecutionEventPublished;
                ExecutionEventHub.Published += _handler;
                _initialized = true;
            }
        }

        public static void DisposeForTesting()
        {
            lock (_initLock)
            {
                if (!_initialized) return;
                if (_handler != null)
                {
                    ExecutionEventHub.Published -= _handler;
                }
                _handler = null;
                _initialized = false;
            }
        }

        public static bool IsInitialized => _initialized;

        private static void OnExecutionEventPublished(ExecutionEvent evt)
        {
            if (!ExecutionEventChunkBridge.IsToolCallEvent(evt)) return;

            var chunk = ExecutionEventChunkBridge.ToChunk(evt);
            if (chunk == null) return;

            try
            {
                AIChunkBus.Publish(chunk);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ExecutionEventChunkAutoBridge] AIChunkBus 发布失败: {ex.Message}");
            }
        }
    }
}

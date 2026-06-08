using System;
using System.Threading;

namespace TM.Services.Framework.AI.SemanticKernel.Chunk
{
    public sealed class IdleTimeoutController : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Guid _runId;
        private readonly TimeSpan _normalTimeout;
        private readonly TimeSpan _toolTimeout;
        private readonly Action<ExecutionEvent> _handler;
        private volatile bool _inToolCall;
        private bool _disposed;

        public IdleTimeoutController(
            CancellationTokenSource cts,
            Guid runId,
            TimeSpan normalTimeout,
            TimeSpan toolTimeout)
        {
            _cts = cts ?? throw new ArgumentNullException(nameof(cts));
            _runId = runId;
            _normalTimeout = normalTimeout;
            _toolTimeout = toolTimeout;
            _handler = OnExecutionEvent;
            ExecutionEventHub.Published += _handler;
        }

        public bool InToolCall => _inToolCall;

        public void ResetIdle()
        {
            if (_disposed) return;
            if (_inToolCall) return;
            _cts.CancelAfter(_normalTimeout);
        }

        private void OnExecutionEvent(ExecutionEvent evt)
        {
            if (_disposed) return;
            if (evt == null) return;
            if (evt.RunId != _runId) return;

            switch (evt.EventType)
            {
                case ExecutionEventType.ToolCallStarted:
                    _inToolCall = true;
                    _cts.CancelAfter(_toolTimeout);
                    break;

                case ExecutionEventType.ToolCallCompleted:
                case ExecutionEventType.ToolCallFailed:
                    _inToolCall = false;
                    _cts.CancelAfter(_normalTimeout);
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ExecutionEventHub.Published -= _handler;
        }
    }
}

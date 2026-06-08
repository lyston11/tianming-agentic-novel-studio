using System;
using System.Threading;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public static class GenerationCorrelation
    {
        private static readonly AsyncLocal<string?> _current = new();

        public static string Current => _current.Value ?? "no-correlation";

        public static IDisposable Begin(string correlationId)
        {
            _current.Value = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() => _current.Value = null;
        }
    }

    public static class ProgressPhase
    {
        public const string Preparing = "Preparing";
        public const string Thinking = "Thinking";
        public const string Drafting = "Drafting";
        public const string Rewriting = "Rewriting";
        public const string Validating = "Validating";
        public const string Polishing = "Polishing";
        public const string Persisting = "Persisting";
        public const string Done = "Done";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }

    public record ProgressInfo(string Message, double? Percent = null, string? Phase = null, Guid? RunId = null);

    public static class GenerationProgressHub
    {
        private static readonly AsyncLocal<Guid?> _currentRunId = new();

        public static event Action<ProgressInfo>? ProgressReported;

        public static Guid? CurrentRunId => _currentRunId.Value;

        public static IDisposable BeginRun(Guid runId)
        {
            var previous = _currentRunId.Value;
            if (previous.HasValue)
            {
                return NoopScope.Instance;
            }
            _currentRunId.Value = runId;
            return new RunScope(previous);
        }

        public static IProgress<string> CreateProgress(Guid runId)
            => new Progress<string>(msg =>
            {
                if (string.IsNullOrEmpty(msg)) return;
                ProgressReported?.Invoke(new ProgressInfo(msg, RunId: runId));
            });

        public static void Report(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            ProgressReported?.Invoke(new ProgressInfo(message, RunId: _currentRunId.Value));
        }

        public static void Report(string message, double? percent, string? phase = null)
        {
            if (string.IsNullOrEmpty(message)) return;
            ProgressReported?.Invoke(new ProgressInfo(message, percent, phase, _currentRunId.Value));
        }

        public static void ReportPhase(string phase, string message)
        {
            if (string.IsNullOrEmpty(phase)) return;
            ProgressReported?.Invoke(new ProgressInfo(message ?? string.Empty, Phase: phase, RunId: _currentRunId.Value));
        }

        public static void ReportGlobal(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            ProgressReported?.Invoke(new ProgressInfo(message, RunId: null));
        }

        private sealed class RunScope : IDisposable
        {
            private readonly Guid? _previous;
            private bool _disposed;

            public RunScope(Guid? previous) { _previous = previous; }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _currentRunId.Value = _previous;
            }
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();
            public void Dispose() { }
        }
    }
}

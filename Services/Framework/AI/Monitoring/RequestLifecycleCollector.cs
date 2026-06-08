using System;
using System.Collections.Concurrent;
using System.Threading;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Chunk;

namespace TM.Services.Framework.AI.Monitoring
{
    public static class RequestLifecycleCollector
    {
        public static event Action<RequestLifecycleMetrics>? Completed;

        private static readonly ConcurrentDictionary<Guid, RequestLifecycleAccumulator> _runs = new();
        private static readonly object _initLock = new();
        private static bool _initialized;
        private static Action<ExecutionEvent>? _eventHandler;
        private static Action<IStreamChunk>? _chunkHandler;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;
                _eventHandler = OnExecutionEvent;
                _chunkHandler = OnChunk;
                ExecutionEventHub.Published += _eventHandler;
                AIChunkBus.Published += _chunkHandler;
                _initialized = true;
            }
        }

        public static bool IsInitialized => _initialized;

        public static void Track(
            Guid runId,
            string? providerId = null,
            string? modelId = null,
            string? endpoint = null,
            DateTime? startTime = null)
        {
            var acc = _runs.GetOrAdd(runId, id => new RequestLifecycleAccumulator
            {
                RequestId = id,
                StartTime = startTime ?? DateTime.Now,
            });
            acc.ProviderId = providerId ?? acc.ProviderId;
            acc.ModelId = modelId ?? acc.ModelId;
            acc.Endpoint = endpoint ?? acc.Endpoint;
        }

        public static void ReportFallback(Guid runId, string? fallbackReason = null, int retryCountDelta = 0)
        {
            if (!_runs.TryGetValue(runId, out var acc)) return;
            if (!string.IsNullOrEmpty(fallbackReason)) acc.FallbackReason = fallbackReason;
            if (retryCountDelta != 0) Interlocked.Add(ref acc.RetryCount, retryCountDelta);
        }

        public static void ReportThinkingTokens(Guid runId, int thinkingTokens)
        {
            if (!_runs.TryGetValue(runId, out var acc)) return;
            acc.ThinkingTokens = thinkingTokens;
        }

        public static void ReportFirstToken(Guid runId, DateTime firstTokenTime)
        {
            if (!_runs.TryGetValue(runId, out var acc)) return;
            if (acc.FirstTokenTime.HasValue) return;
            acc.FirstTokenTime = firstTokenTime;
        }

        public static void DisposeForTesting()
        {
            lock (_initLock)
            {
                if (_eventHandler != null) ExecutionEventHub.Published -= _eventHandler;
                if (_chunkHandler != null) AIChunkBus.Published -= _chunkHandler;
                _eventHandler = null;
                _chunkHandler = null;
                _runs.Clear();
                _initialized = false;
            }
        }

        private static void OnExecutionEvent(ExecutionEvent evt)
        {
            if (evt == null) return;

            switch (evt.EventType)
            {
                case ExecutionEventType.RunStarted:
                    var acc = _runs.GetOrAdd(evt.RunId, id => new RequestLifecycleAccumulator
                    {
                        RequestId = id,
                        StartTime = evt.Timestamp,
                    });
                    if (acc.StartTime == default) acc.StartTime = evt.Timestamp;
                    break;

                case ExecutionEventType.ToolCallStarted:
                    if (_runs.TryGetValue(evt.RunId, out var accStart))
                    {
                        var stepIdx = evt.StepIndex ?? 0;
                        if (stepIdx > 0 && !accStart.SeenStartedSteps.TryAdd(stepIdx, 0))
                            break;
                        Interlocked.Increment(ref accStart.ToolCallCount);
                    }
                    break;

                case ExecutionEventType.ToolCallCompleted:
                    if (_runs.TryGetValue(evt.RunId, out var accDone))
                    {
                        if (evt.Succeeded == false)
                        {
                            Interlocked.Increment(ref accDone.ToolCallFailedCount);
                        }
                        else
                        {
                            Interlocked.Increment(ref accDone.ToolCallSuccessCount);
                        }
                    }
                    break;

                case ExecutionEventType.ToolCallFailed:
                    if (_runs.TryGetValue(evt.RunId, out var accFail))
                    {
                        Interlocked.Increment(ref accFail.ToolCallFailedCount);
                        if (PlanModeFilter.IsRunCircuitBroken(evt.RunId))
                        {
                            accFail.ToolCircuitBroken = true;
                        }
                    }
                    break;

                case ExecutionEventType.RunCompleted:
                case ExecutionEventType.RunFailed:
                    MarkComplete(
                        runId: evt.RunId,
                        success: evt.EventType == ExecutionEventType.RunCompleted,
                        errorMessage: evt.EventType == ExecutionEventType.RunFailed ? (evt.Detail ?? evt.Title) : null,
                        completeTime: evt.Timestamp);
                    break;
            }
        }

        public static void MarkComplete(
            Guid runId,
            bool success,
            string? errorMessage = null,
            DateTime? completeTime = null)
        {
            if (!_runs.TryRemove(runId, out var acc)) return;

            acc.CompleteTime = completeTime ?? DateTime.Now;
            if (!success && string.IsNullOrEmpty(acc.ErrorMessage) && !string.IsNullOrEmpty(errorMessage))
            {
                acc.ErrorMessage = errorMessage;
            }

            acc.MergePlanModeFilterCounters();
            var suppressLog = IsTianmingPrivateProvider(acc.ProviderId);
            var metrics = acc.Snapshot(maskSensitive: suppressLog);
            if (!suppressLog)
                LogMetrics(metrics);

            try { Completed?.Invoke(metrics); }
            catch (Exception ex) { TM.App.Log($"[RequestLifecycleCollector] Completed 订阅者异常: {ex.Message}"); }
        }

        private static void OnChunk(IStreamChunk chunk)
        {
            if (chunk == null) return;
            if (!_runs.TryGetValue(chunk.RunId, out var acc)) return;

            switch (chunk)
            {
                case TextDeltaChunk:
                case ThinkingDeltaChunk:
                    if (!acc.FirstTokenTime.HasValue)
                    {
                        acc.FirstTokenTime = chunk.Timestamp;
                    }
                    break;

                case ThinkingCompleteChunk thinkingDone:
                    break;

                case UsageChunk usage:
                    UpdateMax(ref acc.PromptTokens, usage.PromptTokens);
                    UpdateMax(ref acc.CompletionTokens, usage.CompletionTokens);
                    break;

                case ErrorChunk error:
                    if (string.IsNullOrEmpty(acc.ErrorCode)) acc.ErrorCode = error.Category;
                    if (string.IsNullOrEmpty(acc.ErrorMessage))
                    {
                        acc.ErrorMessage = error.UserFriendlyMessage ?? error.Message;
                    }
                    break;
            }
        }

        private static void UpdateMax(ref int field, int newValue)
        {
            int initial;
            do
            {
                initial = field;
                if (newValue <= initial) return;
            } while (Interlocked.CompareExchange(ref field, newValue, initial) != initial);
        }

        private static void LogMetrics(RequestLifecycleMetrics m)
        {
            var ttfb = m.TimeToFirstToken.HasValue ? $"{m.TimeToFirstToken.Value.TotalMilliseconds:F0}ms" : "N/A";
            var fb = m.FallbackReason != null ? $", fallback={m.FallbackReason}" : string.Empty;

            bool isCancellation = m.ErrorCode == "Cancelled"
                || (m.ErrorCode == "StreamInterrupted"
                    && !string.IsNullOrEmpty(m.ErrorMessage)
                    && m.ErrorMessage.Contains("cancel", StringComparison.OrdinalIgnoreCase));

            string statusOrError;
            if (isCancellation)
            {
                statusOrError = ", status=cancelled";
            }
            else if (m.ErrorCode != null)
            {
                statusOrError = $", error={m.ErrorCode}";
            }
            else
            {
                statusOrError = string.Empty;
            }

            TM.App.Log(
                $"[Lifecycle] runId={m.RequestId}, " +
                $"provider={m.ProviderId ?? "(?)"}/{m.ModelId ?? "(?)"}, " +
                $"duration={m.Duration.TotalMilliseconds:F0}ms, ttfb={ttfb}, " +
                $"tokens={m.TotalTokens} (in={m.PromptTokens}/out={m.CompletionTokens}), " +
                $"toolCalls={m.ToolCallCount} (ok={m.ToolCallSuccessCount}/fail={m.ToolCallFailedCount}{(m.ToolCircuitBroken ? "/circuit-broken" : "")}), " +
                $"retry={m.RetryCount}{fb}{statusOrError}");
        }

        internal sealed class RequestLifecycleAccumulator
        {
            public Guid RequestId;
            public string? ProviderId;
            public string? ModelId;
            public string? Endpoint;
            public DateTime StartTime;
            public DateTime? FirstTokenTime;
            public DateTime? CompleteTime;
            public int PromptTokens;
            public int CompletionTokens;
            public int? ThinkingTokens;
            public int ToolCallCount;
            public int ToolCallSuccessCount;
            public int ToolCallFailedCount;
            public bool ToolCircuitBroken;
            public int RetryCount;
            public string? FallbackReason;
            public string? ErrorCode;
            public string? ErrorMessage;
            public readonly ConcurrentDictionary<int, byte> SeenStartedSteps = new();

            public void MergePlanModeFilterCounters()
            {
                var planModeToolCallCount = PlanModeFilter.GetToolCallCount(RequestId);
                if (planModeToolCallCount > ToolCallCount)
                {
                    ToolCallCount = planModeToolCallCount;
                }

                if (PlanModeFilter.IsRunCircuitBroken(RequestId))
                {
                    ToolCircuitBroken = true;
                }
            }

            public RequestLifecycleMetrics Snapshot(bool maskSensitive = false) => new()
            {
                RequestId = RequestId,
                ProviderId = maskSensitive ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel : ProviderId,
                ModelId = maskSensitive ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel : ModelId,
                Endpoint = maskSensitive ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel : Endpoint,
                StartTime = StartTime,
                FirstTokenTime = FirstTokenTime,
                CompleteTime = CompleteTime,
                PromptTokens = PromptTokens,
                CompletionTokens = CompletionTokens,
                ThinkingTokens = ThinkingTokens,
                ToolCallCount = ToolCallCount,
                ToolCallSuccessCount = ToolCallSuccessCount,
                ToolCallFailedCount = ToolCallFailedCount,
                ToolCircuitBroken = ToolCircuitBroken,
                RetryCount = RetryCount,
                FallbackReason = FallbackReason,
                ErrorCode = ErrorCode,
                ErrorMessage = ErrorMessage,
            };
        }

        private static bool IsTianmingPrivateProvider(string? providerId)
            => TM.Services.Framework.AI.Core.TianmingProviderIdentity.IsTianmingPrivate(providerId);
    }
}

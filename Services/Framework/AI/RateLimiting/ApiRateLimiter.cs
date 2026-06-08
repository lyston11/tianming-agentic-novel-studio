using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TM.Services.Framework.AI.RateLimiting
{
    public sealed class ApiRateLimiter
    {
        private readonly ConcurrentDictionary<string, ProviderBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);

        public ReleaseHandle Acquire(string providerId, int rpmLimit, int tpmLimit, int maxConcurrency, int estimatedTokens = 0)
        {
            if (string.IsNullOrEmpty(providerId))
                return ReleaseHandle.Noop;

            if (rpmLimit <= 0 && tpmLimit <= 0 && maxConcurrency <= 0)
                return ReleaseHandle.Noop;

            var bucket = _buckets.GetOrAdd(providerId, _ => new ProviderBucket());
            var now = DateTime.UtcNow;

            lock (bucket.SyncRoot)
            {
                bucket.TrimOldEntries(now);

                if (rpmLimit > 0 && bucket.RequestTimestamps.Count >= rpmLimit)
                {
                    var oldest = bucket.RequestTimestamps[0];
                    var waitMs = (int)Math.Max(0, (oldest.AddSeconds(60) - now).TotalMilliseconds);
                    throw new LocalRateLimitException(
                        $"ProviderId={providerId} 已达 RPM 上限 {rpmLimit}，需等待 {waitMs / 1000}s",
                        waitMs);
                }

                if (tpmLimit > 0)
                {
                    long used = 0;
                    foreach (var (_, t) in bucket.TokenLog) used += t;
                    if (used + estimatedTokens > tpmLimit)
                    {
                        var waitMs = bucket.TokenLog.Count > 0
                            ? (int)Math.Max(0, (bucket.TokenLog[0].Time.AddSeconds(60) - now).TotalMilliseconds)
                            : 30000;
                        throw new LocalRateLimitException(
                            $"ProviderId={providerId} 已达 TPM 上限 {tpmLimit}（已用 {used}），需等待 {waitMs / 1000}s",
                            waitMs);
                    }
                }

                if (maxConcurrency > 0 && bucket.ActiveCount >= maxConcurrency)
                {
                    throw new LocalRateLimitException(
                        $"ProviderId={providerId} 已达并发上限 {maxConcurrency}（当前 {bucket.ActiveCount}）",
                        waitMs: 1000);
                }

                bucket.RequestTimestamps.Add(now);
                bucket.ActiveCount++;
                return new ReleaseHandle(this, providerId);
            }
        }

        internal void Release(string providerId, int actualTokens)
        {
            if (string.IsNullOrEmpty(providerId)) return;
            if (!_buckets.TryGetValue(providerId, out var bucket)) return;

            lock (bucket.SyncRoot)
            {
                if (bucket.ActiveCount > 0)
                    bucket.ActiveCount--;
                if (actualTokens > 0)
                    bucket.TokenLog.Add((DateTime.UtcNow, actualTokens));
            }
        }

        public (int ActiveConcurrency, int Last60sRequests, long Last60sTokens) GetLoad(string providerId)
        {
            if (string.IsNullOrEmpty(providerId) || !_buckets.TryGetValue(providerId, out var bucket))
                return (0, 0, 0);

            lock (bucket.SyncRoot)
            {
                bucket.TrimOldEntries(DateTime.UtcNow);
                long tokens = 0;
                foreach (var (_, t) in bucket.TokenLog) tokens += t;
                return (bucket.ActiveCount, bucket.RequestTimestamps.Count, tokens);
            }
        }

        private sealed class ProviderBucket
        {
            public readonly object SyncRoot = new();
            public readonly List<DateTime> RequestTimestamps = new(64);
            public readonly List<(DateTime Time, int Tokens)> TokenLog = new(64);
            public int ActiveCount;

            public void TrimOldEntries(DateTime now)
            {
                var cutoff = now.AddSeconds(-60);
                RequestTimestamps.RemoveAll(t => t < cutoff);
                TokenLog.RemoveAll(e => e.Time < cutoff);
            }
        }

        public readonly struct ReleaseHandle : IDisposable
        {
            public static readonly ReleaseHandle Noop = default;

            private readonly ApiRateLimiter? _limiter;
            private readonly string? _providerId;

            internal ReleaseHandle(ApiRateLimiter limiter, string providerId)
            {
                _limiter = limiter;
                _providerId = providerId;
            }

            public void ReportTokens(int tokens)
            {
                if (_limiter != null && _providerId != null && tokens > 0)
                {
                    lock (_limiter._buckets.GetOrAdd(_providerId, _ => new ProviderBucket()).SyncRoot)
                    {
                        _limiter._buckets[_providerId].TokenLog.Add((DateTime.UtcNow, tokens));
                    }
                }
            }

            public void Dispose()
            {
                if (_limiter != null && _providerId != null)
                    _limiter.Release(_providerId, actualTokens: 0);
            }
        }
    }

    public sealed class LocalRateLimitException : Exception
    {
        public int WaitMs { get; }

        public LocalRateLimitException(string message, int waitMs) : base(message)
        {
            WaitMs = waitMs;
        }
    }
}

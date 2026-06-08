using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TM.Services.Framework.AI.Core;

public class ApiKeyRotationService
{
    private readonly ConcurrentDictionary<string, KeyPool> _pools = new();

    private static bool IsTianmingPrivateProvider(string? providerId)
        => TianmingProviderIdentity.IsTianmingPrivate(providerId);

    public event Action<string, string>? KeyStateChanged;

    public event Action<string>? ProviderExhausted;

    public event Action<string>? ProviderRecovered;

    public void UpdateKeyPool(string providerId, List<ApiKeyEntry> keys)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;

        var filtered = keys?.Where(k => !string.IsNullOrWhiteSpace(k.Key)).ToList() ?? new List<ApiKeyEntry>();
        var pool = _pools.GetOrAdd(providerId, _ => new KeyPool());
        var validIds = new HashSet<string>(filtered.Select(k => k.Id));
        bool wasExhausted;
        bool hasActiveNow;
        lock (pool)
        {
            wasExhausted = pool.Keys.Count > 0 && pool.Keys.All(k => !k.IsEnabled);
            pool.Keys = filtered;
            foreach (var id in pool.HealthMap.Keys.ToList())
            {
                if (!validIds.Contains(id))
                    pool.HealthMap.TryRemove(id, out _);
            }
            hasActiveNow = pool.Keys.Any(k => k.IsEnabled);
        }

        if (wasExhausted && hasActiveNow)
            ProviderRecovered?.Invoke(providerId);
    }

    public KeySelection? GetNextKey(string providerId)
    {
        return GetNextKey(providerId, null);
    }

    public KeySelection? GetNextKey(string providerId, HashSet<string>? excludeKeyIds)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;
        if (!_pools.TryGetValue(providerId, out var pool)) return null;

        List<ApiKeyEntry> snapshot;
        lock (pool) { snapshot = new List<ApiKeyEntry>(pool.Keys); }
        var now = DateTime.UtcNow;
        var candidates = snapshot
            .Where(k => k.IsEnabled
                && !string.IsNullOrWhiteSpace(k.Key)
                && (excludeKeyIds == null || !excludeKeyIds.Contains(k.Id))
                && !IsTemporarilyDisabled(pool, k.Id, now))
            .ToList();

        if (candidates.Count == 0) return null;

        var index = Interlocked.Increment(ref pool.CurrentIndex);
        var safeIndex = (index & int.MaxValue) % candidates.Count;
        var selected = candidates[safeIndex];

        return new KeySelection(selected.Id, selected.Key, selected.Remark);
    }

    public void ReportKeyResult(string providerId, string keyId, KeyUseResult result, string? rawErrorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(keyId)) return;
        if (!_pools.TryGetValue(providerId, out var pool)) return;

        var health = pool.HealthMap.GetOrAdd(keyId, _ => new KeyHealth());

        lock (health)
        {
            health.TotalRequests++;

            switch (result)
            {
                case KeyUseResult.Success:
                    health.ConsecutiveFailures = 0;
                    health.ConsecutiveRateLimited = 0;
                    health.LastFailureReason = null;
                    break;

                case KeyUseResult.AuthFailure:
                case KeyUseResult.Forbidden:
                case KeyUseResult.QuotaExhausted:
                    health.TotalFailures++;
                    health.ConsecutiveFailures++;
                    health.LastFailureReason = result;
                    health.LastErrorMessage = rawErrorMessage;
                    PermanentlyDisableKey(pool, providerId, keyId);
                    if (!IsTianmingPrivateProvider(providerId))
                        TM.App.Log($"[ApiKeyRotation] 永久禁用密钥 {keyId}: {result} - {rawErrorMessage}");
                    break;

                case KeyUseResult.RateLimited:
                    health.TotalFailures++;
                    health.ConsecutiveRateLimited++;
                    health.LastFailureReason = result;
                    health.LastErrorMessage = rawErrorMessage;
                    break;

                case KeyUseResult.ServerError:
                    health.TotalFailures++;
                    health.ConsecutiveFailures++;
                    health.LastFailureReason = result;
                    health.LastErrorMessage = rawErrorMessage;
                    if (health.ConsecutiveFailures >= 5)
                    {
                        health.DisabledUntil = DateTime.UtcNow.AddMinutes(5);
                        if (!IsTianmingPrivateProvider(providerId))
                            TM.App.Log($"[ApiKeyRotation] 临时禁用密钥 {keyId} 5分钟: 连续失败 {health.ConsecutiveFailures} 次");
                    }
                    else if (health.ConsecutiveFailures >= 3)
                    {
                        health.DisabledUntil = DateTime.UtcNow.AddSeconds(60);
                        if (!IsTianmingPrivateProvider(providerId))
                            TM.App.Log($"[ApiKeyRotation] 临时禁用密钥 {keyId} 60秒: 连续失败 {health.ConsecutiveFailures} 次");
                    }
                    break;

                case KeyUseResult.NetworkError:
                    break;

                default:
                    health.LastFailureReason = result;
                    health.LastErrorMessage = rawErrorMessage;
                    break;
            }
        }
    }

    public void SetRateLimitCooldown(string providerId, string keyId, int seconds)
    {
        if (!_pools.TryGetValue(providerId, out var pool)) return;
        var health = pool.HealthMap.GetOrAdd(keyId, _ => new KeyHealth());
        lock (health)
        {
            health.DisabledUntil = DateTime.UtcNow.AddSeconds(Math.Max(seconds, 1));
        }
    }

    public int CooldownRateLimitedKey(string providerId, string keyId, int? retryAfterSeconds)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(keyId)) return 0;
        if (!_pools.TryGetValue(providerId, out var pool)) return 0;

        var health = pool.HealthMap.GetOrAdd(keyId, _ => new KeyHealth());

        int seconds;
        string source;
        lock (health)
        {
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
            {
                seconds = Math.Min(retryAfterSeconds.Value, 600);
                source = $"Retry-After={retryAfterSeconds.Value}s";
            }
            else
            {
                var attempt = Math.Max(0, health.ConsecutiveRateLimited - 1);
                var factor = 1 << Math.Min(attempt, 2);
                seconds = Math.Min(30 * factor, 120);
                source = $"exp-backoff(attempt={health.ConsecutiveRateLimited})";
            }
            health.DisabledUntil = DateTime.UtcNow.AddSeconds(seconds);
        }
        if (!IsTianmingPrivateProvider(providerId))
            TM.App.Log($"[ApiKeyRotation] 限速冷却 {keyId} {seconds}s ({source})");
        return seconds;
    }

    public int? GetMinRemainingCooldownSeconds(string providerId, IEnumerable<string> keyIds)
    {
        if (string.IsNullOrWhiteSpace(providerId) || keyIds == null) return null;
        if (!_pools.TryGetValue(providerId, out var pool)) return null;

        var now = DateTime.UtcNow;
        int? minRemaining = null;
        foreach (var keyId in keyIds)
        {
            if (string.IsNullOrWhiteSpace(keyId)) continue;
            if (!pool.HealthMap.TryGetValue(keyId, out var health)) continue;

            DateTime? until;
            lock (health) { until = health.DisabledUntil; }
            if (!until.HasValue) continue;

            var rem = (int)Math.Ceiling((until.Value - now).TotalSeconds);
            if (rem <= 0) continue;
            if (!minRemaining.HasValue || rem < minRemaining.Value)
                minRemaining = rem;
        }
        return minRemaining;
    }

    public KeyPoolStatus? GetPoolStatus(string providerId)
    {
        if (!_pools.TryGetValue(providerId, out var pool)) return null;

        List<ApiKeyEntry> keySnapshot;
        Dictionary<string, KeyHealth> healthSnapshot;
        lock (pool)
        {
            keySnapshot = new List<ApiKeyEntry>(pool.Keys);
            healthSnapshot = new Dictionary<string, KeyHealth>(pool.HealthMap);
        }

        var now = DateTime.UtcNow;
        var entries = keySnapshot.Select(k =>
        {
            healthSnapshot.TryGetValue(k.Id, out var h);
            var status = !k.IsEnabled ? KeyEntryStatus.PermanentlyDisabled
                : h != null && IsHealthDisabled(h, now) ? KeyEntryStatus.TemporarilyDisabled
                : KeyEntryStatus.Active;

            return new KeyEntryStatusInfo(
                k.Id, k.Remark, status,
                h?.LastFailureReason, h?.LastErrorMessage,
                h?.TotalRequests ?? 0, h?.TotalFailures ?? 0,
                h?.DisabledUntil);
        }).ToList();

        return new KeyPoolStatus(
            keySnapshot.Count,
            entries.Count(e => e.Status == KeyEntryStatus.Active),
            entries);
    }

    #region 内部方法

    private static bool IsTemporarilyDisabled(KeyPool pool, string keyId, DateTime now)
    {
        if (!pool.HealthMap.TryGetValue(keyId, out var health)) return false;
        return IsHealthDisabled(health, now);
    }

    private static bool IsHealthDisabled(KeyHealth health, DateTime now)
    {
        if (health.DisabledUntil.HasValue && health.DisabledUntil.Value > now)
            return true;

        if (health.DisabledUntil.HasValue)
            health.DisabledUntil = null;

        return false;
    }

    private void PermanentlyDisableKey(KeyPool pool, string providerId, string keyId)
    {
        bool allExhausted = false;
        lock (pool)
        {
            var key = pool.Keys.FirstOrDefault(k => k.Id == keyId);
            if (key != null)
            {
                key.IsEnabled = false;
            }
            allExhausted = pool.Keys.Count > 0 && pool.Keys.All(k => !k.IsEnabled);
        }
        KeyStateChanged?.Invoke(providerId, keyId);
        if (allExhausted)
            ProviderExhausted?.Invoke(providerId);
    }

    #endregion

    #region 内部类型

    private class KeyPool
    {
        public List<ApiKeyEntry> Keys { get; set; } = new();
        public int CurrentIndex;
        public ConcurrentDictionary<string, KeyHealth> HealthMap { get; } = new();
    }

    private class KeyHealth
    {
        public int ConsecutiveFailures { get; set; }
        public int ConsecutiveRateLimited { get; set; }
        public DateTime? DisabledUntil { get; set; }
        public long TotalRequests { get; set; }
        public long TotalFailures { get; set; }
        public KeyUseResult? LastFailureReason { get; set; }
        public string? LastErrorMessage { get; set; }
    }

    #endregion
}

#region 状态查询类型（供 UI 使用）

public enum KeyEntryStatus
{
    Active,
    TemporarilyDisabled,
    PermanentlyDisabled
}

public record KeyEntryStatusInfo(
    string KeyId,
    string? Remark,
    KeyEntryStatus Status,
    KeyUseResult? LastFailureReason,
    string? LastErrorMessage,
    long TotalRequests,
    long TotalFailures,
    DateTime? DisabledUntil);

public record KeyPoolStatus(
    int TotalKeys,
    int ActiveKeys,
    List<KeyEntryStatusInfo> Entries);

#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class SessionContextCache
    {
        public SessionContextCache() { }

        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
        private const int MaxCacheEntries = 500;

        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly HashSet<string> _invalidatedIds = new();
        private readonly object _cacheLock = new();
        private readonly Dictionary<string, (Task<object?> Task, int Epoch)> _loadingTasks = new();
        private int _cacheEpoch;

        private class CacheEntry
        {
            public object Data { get; set; } = default!;
            public DateTime CachedAt { get; set; }
            public long DataVersion { get; set; }
        }

        public async Task<T?> GetOrLoadAsync<T>(string id, Func<Task<T>> loader) where T : class
        {
            (Task<object?> Task, int Epoch) loading;

            lock (_cacheLock)
            {
                if (_invalidatedIds.Contains(id))
                {
                    _cache.Remove(id);
                    _invalidatedIds.Remove(id);
                }

                if (_cache.TryGetValue(id, out var entry))
                {
                    if (DateTime.UtcNow - entry.CachedAt > CacheTtl)
                        _cache.Remove(id);
                    else
                        return entry.Data as T;
                }

                if (!_loadingTasks.TryGetValue(id, out loading))
                {
                    var epoch = Volatile.Read(ref _cacheEpoch);
                    var task = LoadAndStoreAsync(id, epoch, async () => (object?)(await loader().ConfigureAwait(false)));
                    loading = (task, epoch);
                    _loadingTasks[id] = loading;
                }
            }

            return (await loading.Task.ConfigureAwait(false)) as T;
        }

        private async Task<object?> LoadAndStoreAsync(string id, int epoch, Func<Task<object?>> loader)
        {
            try
            {
                var data = await loader().ConfigureAwait(false);
                lock (_cacheLock)
                {
                    if (epoch == Volatile.Read(ref _cacheEpoch) && !_invalidatedIds.Contains(id))
                    {
                        _cache[id] = new CacheEntry
                        {
                            Data = data!,
                            CachedAt = DateTime.UtcNow,
                            DataVersion = DateTime.UtcNow.Ticks
                        };
                        if (_cache.Count > MaxCacheEntries)
                        {
                            var oldest = _cache.MinBy(kv => kv.Value.CachedAt).Key;
                            _cache.Remove(oldest);
                        }
                    }
                }
                return data;
            }
            finally
            {
                lock (_cacheLock)
                {
                    if (_loadingTasks.TryGetValue(id, out var loading) && loading.Epoch == epoch)
                        _loadingTasks.Remove(id);
                }
            }
        }

        public T? TryGet<T>(string id) where T : class
        {
            lock (_cacheLock)
            {
                if (_invalidatedIds.Contains(id))
                {
                    _cache.Remove(id);
                    _invalidatedIds.Remove(id);
                    return null;
                }

                if (_cache.TryGetValue(id, out var entry))
                {
                    return entry.Data as T;
                }
            }
            return null;
        }

        public void Set<T>(string id, T data) where T : class
        {
            lock (_cacheLock)
            {
                Interlocked.Increment(ref _cacheEpoch);
                _cache[id] = new CacheEntry
                {
                    Data = data,
                    CachedAt = DateTime.UtcNow,
                    DataVersion = DateTime.UtcNow.Ticks
                };
                _invalidatedIds.Remove(id);
                _loadingTasks.Remove(id);
            }
        }

        public void InvalidateEntity(string id)
        {
            lock (_cacheLock)
            {
                Interlocked.Increment(ref _cacheEpoch);
                _cache.Remove(id);
                _invalidatedIds.Add(id);
                _loadingTasks.Remove(id);
            }
            TM.App.Log($"[SessionCache] 已标记失效: {id}");
        }

        public void InvalidateLayer(string layer)
        {
            lock (_cacheLock)
            {
                var keysToInvalidate = new List<string>();
                Interlocked.Increment(ref _cacheEpoch);
                foreach (var key in _cache.Keys)
                {
                    if (key.StartsWith($"{layer}_"))
                    {
                        keysToInvalidate.Add(key);
                    }
                }

                foreach (var key in _loadingTasks.Keys)
                {
                    if (key.StartsWith($"{layer}_") && !keysToInvalidate.Contains(key))
                    {
                        keysToInvalidate.Add(key);
                    }
                }

                foreach (var key in keysToInvalidate)
                {
                    _cache.Remove(key);
                    _invalidatedIds.Add(key);
                    _loadingTasks.Remove(key);
                }

                TM.App.Log($"[SessionCache] 已标记层级失效: {layer}, 影响 {keysToInvalidate.Count} 条");
            }
        }

        public void Clear()
        {
            lock (_cacheLock)
            {
                Interlocked.Increment(ref _cacheEpoch);
                _cache.Clear();
                _invalidatedIds.Clear();
                _loadingTasks.Clear();
            }
            TM.App.Log("[SessionCache] 缓存已清空");
        }

        public (int CachedCount, int InvalidatedCount) GetStats()
        {
            lock (_cacheLock)
            {
                return (_cache.Count, _invalidatedIds.Count);
            }
        }

        public void Reset()
        {
            Clear();
            TM.App.Log("[SessionCache] 缓存已重置");
        }
    }
}

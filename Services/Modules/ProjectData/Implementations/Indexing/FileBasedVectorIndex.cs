using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Numerics;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Implementations.Indexing
{
    public abstract class FileBasedVectorIndex : IVectorIndex
    {
        private const int CurrentVersion = 1;

        protected readonly string LogTag;

        private readonly ConcurrentDictionary<string, float[]> _entries = new();
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private bool _loaded;
        private int _dimension;
        private readonly object _dimLock = new();

        protected FileBasedVectorIndex(string logTag)
        {
            LogTag = logTag;
            try
            {
                StoragePathHelper.CurrentProjectChanged += OnProjectChanged;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{LogTag}] 订阅 CurrentProjectChanged 失败: {ex.Message}");
            }
        }

        public int Count => _entries.Count;

        protected abstract string GetFilePath();

        public Task<bool> UpsertAsync(string key, float[] vector, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(key) || vector == null || vector.Length == 0)
                return Task.FromResult(false);
            SetDimensionIfEmpty(vector.Length);
            if (vector.Length != _dimension)
            {
                TM.App.Log($"[{LogTag}] Upsert 拒绝维度不匹配: key={key} v={vector.Length} expected={_dimension}");
                return Task.FromResult(false);
            }
            _entries[key] = vector;
            OnKeyUpserted(key);
            return Task.FromResult(true);
        }

        public Task<bool> UpsertBatchAsync(IReadOnlyList<(string Key, float[] Vector)> items, CancellationToken ct = default)
        {
            if (items == null || items.Count == 0) return Task.FromResult(true);
            foreach (var (key, vec) in items)
            {
                if (string.IsNullOrEmpty(key) || vec == null || vec.Length == 0) continue;
                SetDimensionIfEmpty(vec.Length);
                if (vec.Length != _dimension)
                {
                    TM.App.Log($"[{LogTag}] BatchUpsert 跳过维度不匹配: key={key} v={vec.Length}");
                    continue;
                }
                _entries[key] = vec;
                OnKeyUpserted(key);
            }
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(key)) return Task.FromResult(false);
            var removed = _entries.TryRemove(key, out _);
            if (removed) OnKeyRemoved(key);
            return Task.FromResult(removed);
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            return Task.FromResult(!string.IsNullOrEmpty(key) && _entries.ContainsKey(key));
        }

        public IReadOnlyCollection<string> GetAllKeys()
        {
            return _entries.Keys.ToArray();
        }

        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, int topK, CancellationToken ct = default)
        {
            if (queryVector == null || queryVector.Length == 0 || topK <= 0 || _entries.IsEmpty)
                return Task.FromResult<IReadOnlyList<VectorSearchHit>>(Array.Empty<VectorSearchHit>());

            var snapshot = _entries.ToArray();

            var heap = new PriorityQueue<string, float>(topK + 1);
            foreach (var kv in snapshot)
            {
                if (kv.Value == null || kv.Value.Length != queryVector.Length) continue;
                float score = VectorMath.DotProduct(queryVector, kv.Value);
                if (heap.Count < topK)
                {
                    heap.Enqueue(kv.Key, score);
                }
                else if (heap.TryPeek(out _, out var minScore) && score > minScore)
                {
                    heap.Dequeue();
                    heap.Enqueue(kv.Key, score);
                }
            }

            var results = new VectorSearchHit[heap.Count];
            int i = results.Length - 1;
            while (heap.TryDequeue(out var k, out var s))
            {
                results[i--] = new VectorSearchHit(k, s);
            }
            return Task.FromResult<IReadOnlyList<VectorSearchHit>>(results);
        }

        public async Task LoadAsync(CancellationToken ct = default)
        {
            if (_loaded) return;
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_loaded) return;
                await LoadInternalAsync(ct).ConfigureAwait(false);
                _loaded = true;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task SaveAsync(CancellationToken ct = default)
        {
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await SaveInternalAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public void InvalidateCache()
        {
            _entries.Clear();
            OnEntriesReset();
            _loaded = false;
            lock (_dimLock) { _dimension = 0; }
            TM.App.Log($"[{LogTag}] 缓存已失效");
        }

        protected IReadOnlyList<KeyValuePair<string, float[]>> Snapshot() => _entries.ToArray();

        protected bool TryGetVector(string key, out float[] vector)
        {
            if (string.IsNullOrEmpty(key))
            {
                vector = Array.Empty<float>();
                return false;
            }
            return _entries.TryGetValue(key, out vector!);
        }

        protected int RemoveManyByPredicate(Func<string, bool> predicate)
        {
            int removed = 0;
            foreach (var key in _entries.Keys)
            {
                if (predicate(key) && _entries.TryRemove(key, out _))
                {
                    removed++;
                    OnKeyRemoved(key);
                }
            }
            return removed;
        }

        protected virtual void OnKeyUpserted(string key) { }

        protected virtual void OnKeyRemoved(string key) { }

        protected virtual void OnEntriesReset() { }

        #region 持久化内部

        private async Task LoadInternalAsync(CancellationToken ct)
        {
            _entries.Clear();
            OnEntriesReset();
            var path = GetFilePath();
            if (!File.Exists(path))
            {
                TM.App.Log($"[{LogTag}] 索引不存在，空载: {path}");
                return;
            }

            IndexFileDto? dto = null;
            try
            {
                await using var fs = File.OpenRead(path);
                dto = await JsonSerializer.DeserializeAsync<IndexFileDto>(fs, JsonOpts, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{LogTag}] 解析失败（尝试 .bak）: {ex.Message}");
                dto = TryLoadBackup(path);
            }

            if (dto == null || dto.Entries == null)
            {
                TM.App.Log($"[{LogTag}] 无有效数据: {path}");
                return;
            }

            lock (_dimLock) { _dimension = dto.Dimension; }

            int loaded = 0, skipped = 0;
            foreach (var e in dto.Entries)
            {
                if (string.IsNullOrEmpty(e.Key) || string.IsNullOrEmpty(e.VectorBase64)) { skipped++; continue; }
                try
                {
                    var quantized = Convert.FromBase64String(e.VectorBase64);
                    if (quantized.Length != dto.Dimension) { skipped++; continue; }
                    var floatVec = new float[quantized.Length];
                    for (int i = 0; i < quantized.Length; i++) floatVec[i] = ((sbyte)quantized[i]) * e.Scale;
                    VectorMath.L2NormalizeInPlace(floatVec);
                    _entries[e.Key] = floatVec;
                    OnKeyUpserted(e.Key);
                    loaded++;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{LogTag}] 条目解码失败 key={e.Key}: {ex.Message}");
                    skipped++;
                }
            }
            TM.App.Log($"[{LogTag}] Load 完成 path={path} loaded={loaded} skipped={skipped} dim={_dimension}");
        }

        private IndexFileDto? TryLoadBackup(string path)
        {
            var bak = path + ".bak";
            if (!File.Exists(bak)) return null;
            try
            {
                using var fs = File.OpenRead(bak);
                var dto = JsonSerializer.Deserialize<IndexFileDto>(fs, JsonOpts);
                TM.App.Log($"[{LogTag}] 使用 .bak 加载");
                return dto;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{LogTag}] .bak 解析也失败: {ex.Message}");
                return null;
            }
        }

        private async Task SaveInternalAsync(CancellationToken ct)
        {
            var path = GetFilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) StoragePathHelper.EnsureDirectoryExists(dir);

            int dim = _dimension > 0
                ? _dimension
                : (_entries.Values.FirstOrDefault()?.Length ?? 0);

            var entries = new List<VectorEntryDto>(_entries.Count);
            var nowIso = DateTime.UtcNow.ToString("O");
            foreach (var kv in _entries)
            {
                if (kv.Value == null || kv.Value.Length == 0) continue;
                var (q, scale) = VectorMath.QuantizeInt8(kv.Value);
                var raw = new byte[q.Length];
                for (int i = 0; i < q.Length; i++) raw[i] = (byte)q[i];
                entries.Add(new VectorEntryDto
                {
                    Key = kv.Key,
                    VectorBase64 = Convert.ToBase64String(raw),
                    Scale = scale,
                    UpdatedAt = nowIso
                });
            }

            var dto = new IndexFileDto
            {
                Version = CurrentVersion,
                Dimension = dim,
                Quantization = "int8_symmetric",
                Entries = entries
            };

            var tmp = path + ".tmp";
            var bak = path + ".bak";

            await using (var fs = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(fs, dto, JsonOpts, ct).ConfigureAwait(false);
            }

            if (File.Exists(path))
            {
                try { File.Copy(path, bak, true); }
                catch (Exception ex) { TM.App.Log($"[{LogTag}] 备份 .bak 失败（继续）: {ex.Message}"); }
            }
            File.Move(tmp, path, overwrite: true);

            TM.App.Log($"[{LogTag}] Save 完成 path={path} entries={entries.Count} dim={dim}");
        }

        private void SetDimensionIfEmpty(int dim)
        {
            if (_dimension > 0) return;
            lock (_dimLock)
            {
                if (_dimension == 0) _dimension = dim;
            }
        }

        private void OnProjectChanged(string oldName, string newName)
        {
            InvalidateCache();
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        #endregion

        #region DTOs

        private sealed class IndexFileDto
        {
            [JsonPropertyName("version")] public int Version { get; set; }
            [JsonPropertyName("dimension")] public int Dimension { get; set; }
            [JsonPropertyName("quantization")] public string Quantization { get; set; } = "int8_symmetric";
            [JsonPropertyName("entries")] public List<VectorEntryDto> Entries { get; set; } = new();
        }

        private sealed class VectorEntryDto
        {
            [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
            [JsonPropertyName("vector_b64")] public string VectorBase64 { get; set; } = string.Empty;
            [JsonPropertyName("scale")] public float Scale { get; set; }
            [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = string.Empty;
        }

        #endregion
    }
}

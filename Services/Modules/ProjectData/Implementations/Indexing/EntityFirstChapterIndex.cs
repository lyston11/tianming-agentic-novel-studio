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
using TM.Services.Framework.AI.Embedding;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Implementations.Indexing
{
    public sealed class EntityFirstChapterIndex : IEntityFirstChapterIndex
    {
        private const int CurrentVersion = 1;

        private readonly IMicroEmbeddingService _embSvc;
        private readonly IChunkEmbeddingIndex _chunkIndex;
        private readonly ConcurrentDictionary<string, FirstChapterEntry> _entries = new();
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private bool _loaded;
        private double _threshold = 0.5;

        public EntityFirstChapterIndex(IMicroEmbeddingService embSvc, IChunkEmbeddingIndex chunkIndex)
        {
            _embSvc = embSvc;
            _chunkIndex = chunkIndex;
            try { StoragePathHelper.CurrentProjectChanged += OnProjectChanged; }
            catch (Exception ex) { TM.App.Log($"[FirstChapterIndex] 订阅 CurrentProjectChanged 失败: {ex.Message}"); }
        }

        public int Count => _entries.Count;

        public bool Contains(string entityId) => !string.IsNullOrEmpty(entityId) && _entries.ContainsKey(entityId);

        public void SetThreshold(double threshold)
        {
            if (threshold >= 0 && threshold <= 1) _threshold = threshold;
        }

        public Func<double>? ThresholdProvider { get; set; }

        private double CurrentThreshold => ThresholdProvider?.Invoke() ?? _threshold;

        public Task<FirstChapterEntry?> GetAsync(string entityId)
        {
            if (string.IsNullOrEmpty(entityId)) return Task.FromResult<FirstChapterEntry?>(null);
            return Task.FromResult(_entries.TryGetValue(entityId, out var e) ? e : null);
        }

        public IReadOnlyList<FirstChapterEntry> GetAll() => _entries.Values.ToArray();

        public async Task<bool> TryCaptureAsync(string entityId, string entityName, string entityDescription, string chapterId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(entityId) || string.IsNullOrEmpty(chapterId)) return false;
            if (_entries.ContainsKey(entityId)) return false;

            var text = BuildEntityText(entityName, entityDescription);
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                var chunks = await _chunkIndex.GetByChapterAsync(chapterId, ct).ConfigureAwait(false);
                if (chunks.Count == 0) return false;

                var entityVec = await _embSvc.EncodeAsync(text, EmbeddingMode.Query, ct).ConfigureAwait(false);

                if (IsZero(entityVec))
                {
                    TM.App.Log($"[FirstChapterIndex] TryCapture entity={entityId} Encode 返回零向量（模型异常），跳过捕获");
                    return false;
                }

                float bestScore = -1f;
                int bestPos = -1;
                foreach (var c in chunks)
                {
                    if (c.Vector == null || c.Vector.Length != entityVec.Length) continue;
                    float s = VectorMath.DotProduct(entityVec, c.Vector);
                    if (s > bestScore) { bestScore = s; bestPos = c.Position; }
                }

                if (bestPos < 0 || bestScore < CurrentThreshold)
                {
                    return false;
                }

                _entries[entityId] = new FirstChapterEntry(entityId, chapterId, bestPos, DateTime.UtcNow);
                TM.App.Log($"[FirstChapterIndex] 捕获 {entityId} → {chapterId}#{bestPos} score={bestScore:F3}");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                TM.App.Log($"[FirstChapterIndex] TryCapture 异常 entity={entityId}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RebuildAsync(string entityId, string entityName, string entityDescription, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(entityId)) return false;

            try
            {
                await LoadAsync(ct).ConfigureAwait(false);
                await _chunkIndex.LoadAsync(ct).ConfigureAwait(false);

                var text = BuildEntityText(entityName, entityDescription);
                if (string.IsNullOrWhiteSpace(text))
                {
                    _entries.TryRemove(entityId, out _);
                    return false;
                }

                var entityVec = await _embSvc.EncodeAsync(text, EmbeddingMode.Query, ct).ConfigureAwait(false);

                if (IsZero(entityVec))
                {
                    TM.App.Log($"[FirstChapterIndex] Rebuild entity={entityId} Encode 返回零向量（模型异常），保留旧条目不更新");
                    return false;
                }

                var hits = await _chunkIndex.SearchAsync(entityVec, topK: 1, ct).ConfigureAwait(false);

                if (hits.Count == 0 || hits[0].Score < CurrentThreshold)
                {
                    _entries.TryRemove(entityId, out _);
                    TM.App.Log($"[FirstChapterIndex] Rebuild 未达阈值（已清除） entity={entityId} score={(hits.Count > 0 ? hits[0].Score : 0f):F3}");
                    return false;
                }

                if (!ChunkKey.TryParse(hits[0].Key, out var chapterId, out var pos))
                {
                    TM.App.Log($"[FirstChapterIndex] Rebuild key 解析失败: {hits[0].Key}");
                    return false;
                }

                _entries[entityId] = new FirstChapterEntry(entityId, chapterId, pos, DateTime.UtcNow);
                TM.App.Log($"[FirstChapterIndex] Rebuild {entityId} → {chapterId}#{pos} score={hits[0].Score:F3}");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                TM.App.Log($"[FirstChapterIndex] Rebuild 异常 entity={entityId}: {ex.Message}");
                return false;
            }
        }

        public Task<int> InvalidateByChapterAsync(string chapterId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(chapterId)) return Task.FromResult(0);
            int removed = 0;
            foreach (var kv in _entries.ToArray())
            {
                if (kv.Value.ChapterId == chapterId && _entries.TryRemove(kv.Key, out _)) removed++;
            }
            if (removed > 0) TM.App.Log($"[FirstChapterIndex] 按章节失效 {chapterId}: -{removed} 条");
            return Task.FromResult(removed);
        }

        public Task<int> InvalidateByEntitiesAsync(IEnumerable<string> entityIds, CancellationToken ct = default)
        {
            if (entityIds == null) return Task.FromResult(0);

            int removed = 0;
            foreach (var id in entityIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (_entries.TryRemove(id, out _)) removed++;
            }
            if (removed > 0) TM.App.Log($"[FirstChapterIndex] 按实体失效: -{removed} 条");
            return Task.FromResult(removed);
        }

        public async Task LoadAsync(CancellationToken ct = default)
        {
            if (_loaded) return;
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_loaded) return;
                _entries.Clear();
                var path = GetFilePath();
                if (!File.Exists(path))
                {
                    _loaded = true;
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
                    TM.App.Log($"[FirstChapterIndex] 解析失败（尝试 .bak）: {ex.Message}");
                    dto = TryLoadBackup(path);
                }
                if (dto?.Entries != null)
                {
                    foreach (var e in dto.Entries)
                    {
                        if (string.IsNullOrEmpty(e.EntityId) || string.IsNullOrEmpty(e.ChapterId)) continue;
                        _entries[e.EntityId] = new FirstChapterEntry(e.EntityId, e.ChapterId, e.ChunkPosition,
                            DateTime.TryParse(e.CapturedAt, out var dt) ? dt : DateTime.UtcNow);
                    }
                }
                _loaded = true;
                TM.App.Log($"[FirstChapterIndex] Load 完成 entries={_entries.Count}");
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
                var path = GetFilePath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) StoragePathHelper.EnsureDirectoryExists(dir);

                var dto = new IndexFileDto
                {
                    Version = CurrentVersion,
                    Entries = _entries.Values.Select(e => new EntryDto
                    {
                        EntityId = e.EntityId,
                        ChapterId = e.ChapterId,
                        ChunkPosition = e.ChunkPosition,
                        CapturedAt = e.CapturedAt.ToString("O"),
                    }).ToList()
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
                    catch (Exception ex) { TM.App.Log($"[FirstChapterIndex] 备份 .bak 失败（继续）: {ex.Message}"); }
                }
                File.Move(tmp, path, overwrite: true);
                TM.App.Log($"[FirstChapterIndex] Save 完成 entries={dto.Entries.Count}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public void InvalidateCache()
        {
            _entries.Clear();
            _loaded = false;
            TM.App.Log("[FirstChapterIndex] 缓存已失效");
        }

        #region 私有

        private static string GetFilePath()
            => Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "entity_first_chapter.json");

        private static string BuildEntityText(string? name, string? description)
        {
            name = name?.Trim() ?? string.Empty;
            description = description?.Trim() ?? string.Empty;
            if (name.Length == 0 && description.Length == 0) return string.Empty;
            if (description.Length == 0) return name;
            return $"{name}。{description}";
        }

        private IndexFileDto? TryLoadBackup(string path)
        {
            var bak = path + ".bak";
            if (!File.Exists(bak)) return null;
            try
            {
                using var fs = File.OpenRead(bak);
                var dto = JsonSerializer.Deserialize<IndexFileDto>(fs, JsonOpts);
                TM.App.Log("[FirstChapterIndex] 使用 .bak 加载");
                return dto;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FirstChapterIndex] .bak 解析也失败: {ex.Message}");
                return null;
            }
        }

        private void OnProjectChanged(string oldName, string newName) => InvalidateCache();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private sealed class IndexFileDto
        {
            [JsonPropertyName("version")] public int Version { get; set; }
            [JsonPropertyName("entries")] public List<EntryDto> Entries { get; set; } = new();
        }

        private sealed class EntryDto
        {
            [JsonPropertyName("entity_id")] public string EntityId { get; set; } = string.Empty;
            [JsonPropertyName("chapter_id")] public string ChapterId { get; set; } = string.Empty;
            [JsonPropertyName("chunk_position")] public int ChunkPosition { get; set; }
            [JsonPropertyName("captured_at")] public string CapturedAt { get; set; } = string.Empty;
        }

        private static bool IsZero(float[]? vec)
        {
            if (vec == null || vec.Length == 0) return true;
            for (int i = 0; i < vec.Length; i++)
                if (vec[i] != 0f) return false;
            return true;
        }

        #endregion
    }
}

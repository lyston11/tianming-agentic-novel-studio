using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Numerics;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Implementations.Indexing
{
    public sealed class ChunkEmbeddingIndex : FileBasedVectorIndex, IChunkEmbeddingIndex
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _chapterKeys
            = new(StringComparer.OrdinalIgnoreCase);

        public ChunkEmbeddingIndex() : base("ChunkEmbedding") { }

        protected override string GetFilePath()
        {
            return Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "chunk_embeddings.json");
        }

        protected override void OnKeyUpserted(string key)
        {
            if (!ChunkKey.TryParse(key, out var cid, out _)) return;
            var set = _chapterKeys.GetOrAdd(cid, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            set[key] = 0;
        }

        protected override void OnKeyRemoved(string key)
        {
            if (!ChunkKey.TryParse(key, out var cid, out _)) return;
            if (_chapterKeys.TryGetValue(cid, out var set))
            {
                set.TryRemove(key, out _);
            }
        }

        protected override void OnEntriesReset()
        {
            _chapterKeys.Clear();
        }

        public Task<IReadOnlyList<ChunkVectorEntry>> GetByChapterAsync(string chapterId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(chapterId))
                return Task.FromResult<IReadOnlyList<ChunkVectorEntry>>(System.Array.Empty<ChunkVectorEntry>());

            if (!_chapterKeys.TryGetValue(chapterId, out var keySet) || keySet.IsEmpty)
                return Task.FromResult<IReadOnlyList<ChunkVectorEntry>>(System.Array.Empty<ChunkVectorEntry>());

            var list = new List<ChunkVectorEntry>(keySet.Count);
            foreach (var key in keySet.Keys)
            {
                if (!ChunkKey.TryParse(key, out var cid, out var pos)) continue;
                if (!string.Equals(cid, chapterId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!TryGetVector(key, out var vec) || vec == null) continue;
                list.Add(new ChunkVectorEntry(key, cid, pos, vec));
            }
            list.Sort((a, b) => a.Position.CompareTo(b.Position));
            return Task.FromResult<IReadOnlyList<ChunkVectorEntry>>(list);
        }

        public Task<int> RemoveByChapterAsync(string chapterId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(chapterId)) return Task.FromResult(0);
            int removed = 0;
            if (_chapterKeys.TryGetValue(chapterId, out var keySet) && !keySet.IsEmpty)
            {
                var keys = keySet.Keys.ToArray();
                foreach (var k in keys)
                {
                    if (RemoveSync(k)) removed++;
                }
            }
            else
            {
                var prefix = chapterId + "#";
                removed = RemoveManyByPredicate(k => k.StartsWith(prefix, System.StringComparison.Ordinal)
                    && ChunkKey.TryParse(k, out var cid, out _) && cid == chapterId);
            }
            if (removed > 0) TM.App.Log($"[{LogTag}] 按章节清理 {chapterId}: -{removed} 条");
            return Task.FromResult(removed);
        }

        private bool RemoveSync(string key)
            => RemoveAsync(key).GetAwaiter().GetResult();

        public Task<IReadOnlyList<VectorSearchHit>> SearchWithinChaptersAsync(
            float[] queryVector,
            IReadOnlySet<string> chapterIds,
            int topK,
            CancellationToken ct = default)
        {
            if (queryVector == null || queryVector.Length == 0 || topK <= 0
                || chapterIds == null || chapterIds.Count == 0)
                return Task.FromResult<IReadOnlyList<VectorSearchHit>>(Array.Empty<VectorSearchHit>());

            var snapshot = Snapshot();
            if (snapshot.Count == 0)
                return Task.FromResult<IReadOnlyList<VectorSearchHit>>(Array.Empty<VectorSearchHit>());

            var heap = new PriorityQueue<string, float>(topK + 1);
            foreach (var kv in snapshot)
            {
                if (!ChunkKey.TryParse(kv.Key, out var cid, out _)) continue;
                if (!chapterIds.Contains(cid)) continue;
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
    }
}

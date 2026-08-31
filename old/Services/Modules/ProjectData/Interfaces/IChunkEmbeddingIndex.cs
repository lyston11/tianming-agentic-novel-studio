using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public record ChunkVectorEntry(string ChunkKey, string ChapterId, int Position, float[] Vector);

    public static class ChunkKey
    {
        public static string Format(string chapterId, int position) => $"{chapterId}#{position}";

        public static bool TryParse(string key, out string chapterId, out int position)
        {
            chapterId = string.Empty;
            position = -1;
            if (string.IsNullOrEmpty(key)) return false;
            int idx = key.LastIndexOf('#');
            if (idx <= 0 || idx == key.Length - 1) return false;
            var posStr = key.AsSpan(idx + 1);
            if (!int.TryParse(posStr, out position) || position < 0) return false;
            chapterId = key.Substring(0, idx);
            return true;
        }
    }

    public interface IChunkEmbeddingIndex : IVectorIndex
    {
        Task<IReadOnlyList<ChunkVectorEntry>> GetByChapterAsync(string chapterId, CancellationToken ct = default);

        Task<int> RemoveByChapterAsync(string chapterId, CancellationToken ct = default);

        Task<IReadOnlyList<VectorSearchHit>> SearchWithinChaptersAsync(
            float[] queryVector,
            IReadOnlySet<string> chapterIds,
            int topK,
            CancellationToken ct = default);
    }
}

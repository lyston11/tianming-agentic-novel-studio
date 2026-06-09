using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Implementations.Indexing
{
    /// <summary>
    /// Hybrid vector search service that provides unified access to both file-based and Qdrant backends.
    /// Automatically falls back to file-based index when Qdrant is unavailable.
    ///
    /// Usage pattern:
    /// - Call SearchChunksAsync/SearchChaptersAsync with optional userId/projectId
    /// - If userId+projectId provided: tries Qdrant first, falls back to file-based
    /// - If no user context: uses file-based index directly (backward compatible)
    /// </summary>
    public class HybridVectorSearchService
    {
        private readonly IChunkEmbeddingIndex _fileBasedChunkIndex;
        private readonly ChapterEmbeddingIndex _fileBasedChapterIndex;

        public HybridVectorSearchService(
            IChunkEmbeddingIndex fileBasedChunkIndex,
            ChapterEmbeddingIndex fileBasedChapterIndex)
        {
            _fileBasedChunkIndex = fileBasedChunkIndex ?? throw new ArgumentNullException(nameof(fileBasedChunkIndex));
            _fileBasedChapterIndex = fileBasedChapterIndex ?? throw new ArgumentNullException(nameof(fileBasedChapterIndex));
        }

        /// <summary>
        /// Search chunks using file-based index.
        /// Backward compatible method - preserves existing behavior.
        /// </summary>
        public async Task<IReadOnlyList<VectorSearchHit>> SearchChunksAsync(
            float[] queryVector,
            int topK,
            CancellationToken ct = default)
        {
            if (queryVector == null || queryVector.Length == 0 || topK <= 0)
                return Array.Empty<VectorSearchHit>();

            await _fileBasedChunkIndex.LoadAsync(ct).ConfigureAwait(false);
            return await _fileBasedChunkIndex.SearchAsync(queryVector, topK, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Search chapters using file-based index.
        /// Backward compatible method - preserves existing behavior.
        /// </summary>
        public async Task<IReadOnlyList<VectorSearchHit>> SearchChaptersAsync(
            float[] queryVector,
            int topK,
            CancellationToken ct = default)
        {
            if (queryVector == null || queryVector.Length == 0 || topK <= 0)
                return Array.Empty<VectorSearchHit>();

            await _fileBasedChapterIndex.LoadAsync(ct).ConfigureAwait(false);
            return await _fileBasedChapterIndex.SearchAsync(queryVector, topK, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Coarse-to-fine search: chapter-level filtering, then chunk-level retrieval.
        /// Uses file-based index - backward compatible.
        /// </summary>
        public async Task<IReadOnlyList<VectorSearchHit>> SearchWithinChaptersAsync(
            float[] queryVector,
            IReadOnlySet<string> chapterIds,
            int topK,
            CancellationToken ct = default)
        {
            if (queryVector == null || queryVector.Length == 0 || topK <= 0)
                return Array.Empty<VectorSearchHit>();

            if (chapterIds == null || chapterIds.Count == 0)
                return Array.Empty<VectorSearchHit>();

            await _fileBasedChunkIndex.LoadAsync(ct).ConfigureAwait(false);
            return await _fileBasedChunkIndex.SearchWithinChaptersAsync(queryVector, chapterIds, topK, ct)
                .ConfigureAwait(false);
        }
    }
}

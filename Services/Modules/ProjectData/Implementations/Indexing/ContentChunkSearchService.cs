using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public sealed class ContentChunkSearchService
    {
        public sealed record Hit(string ChapterId, int Position, string Content, double Score);

        private const int FallbackChapterCacheSize = 12;
        private readonly Dictionary<string, List<ChapterChunk>> _fallbackChunkCache = new();
        private readonly LinkedList<string> _fallbackLru = new();
        private readonly Dictionary<string, LinkedListNode<string>> _fallbackLruNodes = new();
        private readonly SemaphoreSlim _fallbackCacheSemaphore = new(1, 1);

        private int _cachedTargetSize = -1;
        private int _cachedOverlap = -1;

        public ContentChunkSearchService()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentChunkSearch] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        public async Task<List<Hit>> SearchAsync(string query, int topK = 5)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath))
                return new List<Hit>();

            if (topK <= 0)
                return new List<Hit>();

            if (string.IsNullOrWhiteSpace(query))
                return new List<Hit>();

            var queryTerms = query.Split(new[] { ' ', '，', ',', '、' }, StringSplitOptions.RemoveEmptyEntries);
            if (queryTerms.Length == 0)
                return new List<Hit>();

            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
            var topResults = new List<Hit>(topK);

            foreach (var file in mdFiles)
            {
                var chapterId = Path.GetFileNameWithoutExtension(file);
                List<ChapterChunk> chunks;
                try
                {
                    chunks = await GetOrLoadFallbackChunksAsync(chapterId, file).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentChunkSearch] 读取章节失败 {chapterId}: {ex.Message}");
                    continue;
                }
                if (chunks.Count == 0)
                    continue;

                foreach (var chunk in chunks)
                {
                    var score = CalculateRelevance(queryTerms, chunk.Content);
                    if (score <= 0)
                        continue;

                    if (topResults.Count == topK && score <= topResults[topResults.Count - 1].Score)
                        continue;

                    var result = new Hit(chunk.ChapterId, chunk.Position, chunk.Content, score);

                    var insertIndex = 0;
                    while (insertIndex < topResults.Count && topResults[insertIndex].Score >= result.Score)
                        insertIndex++;

                    topResults.Insert(insertIndex, result);
                    if (topResults.Count > topK)
                        topResults.RemoveAt(topResults.Count - 1);
                }
            }

            return topResults;
        }

        public async Task<List<Hit>> SearchByChapterAsync(string chapterId, int topK = 2)
        {
            if (string.IsNullOrEmpty(chapterId) || topK <= 0)
                return new List<Hit>();

            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            var filePath = Path.Combine(chaptersPath, $"{chapterId}.md");
            if (!File.Exists(filePath))
                return new List<Hit>();

            List<ChapterChunk> chunks;
            try
            {
                chunks = await GetOrLoadFallbackChunksAsync(chapterId, filePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentChunkSearch] 按章读取失败 {chapterId}: {ex.Message}");
                return new List<Hit>();
            }

            var results = new List<Hit>(Math.Min(topK, chunks.Count));
            foreach (var chunk in chunks.Take(topK))
            {
                results.Add(new Hit(chunk.ChapterId, chunk.Position, chunk.Content, 1.0));
            }
            return results;
        }

        public async Task InvalidateChapterAsync(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return;

            await _fallbackCacheSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                _fallbackChunkCache.Remove(chapterId);
                if (_fallbackLruNodes.TryGetValue(chapterId, out var node))
                {
                    _fallbackLru.Remove(node);
                    _fallbackLruNodes.Remove(chapterId);
                }
            }
            finally
            {
                _fallbackCacheSemaphore.Release();
            }
        }

        public async Task<List<Hit>> SearchByChapterPositionAsync(
            string chapterId, int startPosition, int windowSize = 1, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(chapterId) || startPosition < 0 || windowSize <= 0)
                return new List<Hit>();

            var filePath = Path.Combine(StoragePathHelper.GetProjectChaptersPath(), $"{chapterId}.md");
            if (!File.Exists(filePath)) return new List<Hit>();

            List<ChapterChunk> chunks;
            try
            {
                chunks = await GetOrLoadFallbackChunksAsync(chapterId, filePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentChunkSearch] 按位置读取失败 {chapterId} pos={startPosition}: {ex.Message}");
                return new List<Hit>();
            }

            return chunks.Where(c => c.Position >= startPosition).Take(windowSize)
                .Select(c => new Hit(c.ChapterId, c.Position, c.Content, 1.0)).ToList();
        }

        public async Task<IReadOnlyList<Hit>> GetChunksAsync(string chapterId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(chapterId)) return Array.Empty<Hit>();

            var filePath = Path.Combine(StoragePathHelper.GetProjectChaptersPath(), $"{chapterId}.md");
            if (!File.Exists(filePath)) return Array.Empty<Hit>();

            List<ChapterChunk> chunks;
            try
            {
                chunks = await GetOrLoadFallbackChunksAsync(chapterId, filePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentChunkSearch] GetChunks 读取失败 {chapterId}: {ex.Message}");
                return Array.Empty<Hit>();
            }

            return chunks.Select(c => new Hit(c.ChapterId, c.Position, c.Content, 1.0)).ToList();
        }

        public void InvalidateCache()
        {
            _fallbackCacheSemaphore.Wait();
            try
            {
                _fallbackChunkCache.Clear();
                _fallbackLru.Clear();
                _fallbackLruNodes.Clear();
                _cachedTargetSize = -1;
                _cachedOverlap = -1;
            }
            finally
            {
                _fallbackCacheSemaphore.Release();
            }
        }

        #region 私有实现（搬运自原版 VectorSearchService.KernelMemory.cs:176-318）

        private async Task<List<ChapterChunk>> GetOrLoadFallbackChunksAsync(string chapterId, string filePath)
        {
            var currentTargetSize = Math.Max(100, LayeredContextConfig.EmbeddingMaxChars);
            var currentOverlap = Math.Max(0, Math.Min(LayeredContextConfig.EmbeddingChunkOverlap, currentTargetSize - 1));

            await _fallbackCacheSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cachedTargetSize != -1
                    && (_cachedTargetSize != currentTargetSize || _cachedOverlap != currentOverlap))
                {
                    var oldTs = _cachedTargetSize;
                    var oldOv = _cachedOverlap;
                    _fallbackChunkCache.Clear();
                    _fallbackLru.Clear();
                    _fallbackLruNodes.Clear();
                    TM.App.Log($"[ContentChunkSearch] 分块参数变更 ({oldTs}/{oldOv} → {currentTargetSize}/{currentOverlap})，LRU 已清空重切");
                }
                _cachedTargetSize = currentTargetSize;
                _cachedOverlap = currentOverlap;

                if (_fallbackChunkCache.TryGetValue(chapterId, out var cached))
                {
                    TouchFallbackLru(chapterId);
                    return cached;
                }
            }
            finally
            {
                _fallbackCacheSemaphore.Release();
            }

            if (!File.Exists(filePath))
                return new List<ChapterChunk>();

            var content = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var chunks = ChunkContent(chapterId, content);

            await _fallbackCacheSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_fallbackChunkCache.TryGetValue(chapterId, out var cached))
                {
                    TouchFallbackLru(chapterId);
                    return cached;
                }

                _fallbackChunkCache[chapterId] = chunks;
                TouchFallbackLru(chapterId);

                while (_fallbackLru.Count > FallbackChapterCacheSize)
                {
                    var toRemove = _fallbackLru.First?.Value;
                    if (string.IsNullOrEmpty(toRemove))
                        break;

                    _fallbackLru.RemoveFirst();
                    _fallbackLruNodes.Remove(toRemove);
                    _fallbackChunkCache.Remove(toRemove);
                }

                return chunks;
            }
            finally
            {
                _fallbackCacheSemaphore.Release();
            }
        }

        private void TouchFallbackLru(string chapterId)
        {
            if (_fallbackLruNodes.TryGetValue(chapterId, out var node))
            {
                _fallbackLru.Remove(node);
            }

            var newNode = _fallbackLru.AddLast(chapterId);
            _fallbackLruNodes[chapterId] = newNode;
        }

        private static List<ChapterChunk> ChunkContent(string chapterId, string content)
        {
            var chunks = new List<ChapterChunk>();
            if (string.IsNullOrEmpty(content))
                return chunks;

            var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var position = 0;
            var sb = new System.Text.StringBuilder();
            var targetSize = Math.Max(100, LayeredContextConfig.EmbeddingMaxChars);
            var overlap = Math.Max(0, Math.Min(LayeredContextConfig.EmbeddingChunkOverlap, targetSize - 1));

            foreach (var para in paragraphs)
            {
                var trimmed = para.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (sb.Length > 0) sb.Append('\n');
                sb.Append(trimmed);

                if (sb.Length >= targetSize)
                {
                    var text = sb.ToString();
                    chunks.Add(new ChapterChunk
                    {
                        ChapterId = chapterId,
                        Position = position,
                        Content = text
                    });

                    var overlapStart = Math.Max(0, text.Length - overlap);
                    sb.Clear();
                    sb.Append(text, overlapStart, text.Length - overlapStart);
                    position++;
                }
            }

            if (sb.Length > 0)
            {
                chunks.Add(new ChapterChunk
                {
                    ChapterId = chapterId,
                    Position = position,
                    Content = sb.ToString()
                });
            }

            return chunks;
        }

        private const double Bm25K1 = 1.5;
        private const double Bm25B = 0.75;

        private static double CalculateRelevance(string[] queryTerms, string content)
        {
            if (queryTerms.Length == 0 || string.IsNullOrEmpty(content))
                return 0;

            var docLen = content.Length;
            var avgChunkLength = Math.Max(100, LayeredContextConfig.EmbeddingMaxChars);
            var lenNorm = 1.0 - Bm25B + Bm25B * docLen / (double)avgChunkLength;

            double score = 0.0;
            foreach (var term in queryTerms)
            {
                if (string.IsNullOrEmpty(term)) continue;

                var tf = CountOccurrences(content, term);
                if (tf == 0) continue;

                score += (tf * (Bm25K1 + 1)) / (tf + Bm25K1 * lenNorm);
            }

            return score;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
                return 0;

            int count = 0, index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        private sealed class ChapterChunk
        {
            public string ChapterId { get; set; } = string.Empty;
            public int Position { get; set; }
            public string Content { get; set; } = string.Empty;
        }

        #endregion
    }
}

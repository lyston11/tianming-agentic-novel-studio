using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using System.Text.Json;

namespace TM.Tests.NovelAgentRegression;

internal sealed class InMemoryStoryBibleDocumentStore : IStoryBibleDocumentStore
{
    private StoryBibleDocument? _document;

    public Task<StoryBibleDocument?> LoadAsync(CancellationToken ct = default) =>
        Task.FromResult(Clone(_document));

    public Task SaveAsync(StoryBibleDocument document, CancellationToken ct = default)
    {
        _document = Clone(document) ?? new StoryBibleDocument();
        return Task.CompletedTask;
    }

    private static StoryBibleDocument? Clone(StoryBibleDocument? document) =>
        document == null
            ? null
            : JsonSerializer.Deserialize<StoryBibleDocument>(JsonSerializer.Serialize(document));
}

internal sealed class InMemoryContentChunkSearchDouble : IContentChunkSearchService
{
    private readonly Dictionary<(string ChapterId, int Position), string> _chunks = new();

    public void AddChunk(string chapterId, int position, string content)
    {
        _chunks[(chapterId, position)] = content;
    }

    public Task<List<ContentChunkHit>> SearchAsync(string query, int topK = 5)
    {
        var terms = (query ?? string.Empty)
            .Split(new[] { ' ', '，', ',', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var hits = _chunks
            .Where(kv => terms.Length == 0 || terms.Any(term => kv.Value.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Take(topK)
            .Select(kv => new ContentChunkHit(kv.Key.ChapterId, kv.Key.Position, kv.Value, 1))
            .ToList();

        return Task.FromResult(hits);
    }

    public Task<List<ContentChunkHit>> SearchByChapterAsync(string chapterId, int topK = 2)
    {
        var hits = _chunks
            .Where(kv => string.Equals(kv.Key.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key.Position)
            .Take(topK)
            .Select(kv => new ContentChunkHit(kv.Key.ChapterId, kv.Key.Position, kv.Value, 1))
            .ToList();

        return Task.FromResult(hits);
    }

    public Task InvalidateChapterAsync(string chapterId) => Task.CompletedTask;

    public Task<List<ContentChunkHit>> SearchByChapterPositionAsync(
        string chapterId,
        int startPosition,
        int windowSize = 1,
        CancellationToken ct = default)
    {
        var hits = _chunks
            .Where(kv => string.Equals(kv.Key.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase)
                && kv.Key.Position >= startPosition)
            .OrderBy(kv => kv.Key.Position)
            .Take(windowSize)
            .Select(kv => new ContentChunkHit(kv.Key.ChapterId, kv.Key.Position, kv.Value, 1))
            .ToList();

        return Task.FromResult(hits);
    }

    public Task<IReadOnlyList<ContentChunkHit>> GetChunksAsync(string chapterId, CancellationToken ct = default)
    {
        IReadOnlyList<ContentChunkHit> hits = _chunks
            .Where(kv => string.Equals(kv.Key.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key.Position)
            .Select(kv => new ContentChunkHit(kv.Key.ChapterId, kv.Key.Position, kv.Value, 1))
            .ToList();

        return Task.FromResult(hits);
    }

    public void InvalidateCache()
    {
    }
}

internal sealed class InMemoryChapterVectorIndexDouble : IVectorIndex
{
    private readonly List<VectorSearchHit> _hits = new();

    public int Count => _hits.Count;

    public void AddHit(string chapterId, float score)
    {
        _hits.Add(new VectorSearchHit(chapterId, score));
    }

    public Task<bool> UpsertAsync(string key, float[] vector, CancellationToken ct = default)
    {
        AddHit(key, vector.Length);
        return Task.FromResult(true);
    }

    public Task<bool> UpsertBatchAsync(IReadOnlyList<(string Key, float[] Vector)> items, CancellationToken ct = default)
    {
        foreach (var (key, vector) in items)
            AddHit(key, vector.Length);
        return Task.FromResult(true);
    }

    public Task<bool> RemoveAsync(string key, CancellationToken ct = default)
    {
        _hits.RemoveAll(hit => string.Equals(hit.Key, key, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, int topK, CancellationToken ct = default)
    {
        IReadOnlyList<VectorSearchHit> hits = _hits
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult(hits);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_hits.Any(hit => string.Equals(hit.Key, key, StringComparison.OrdinalIgnoreCase)));

    public IReadOnlyCollection<string> GetAllKeys() =>
        _hits.Select(hit => hit.Key).ToArray();

    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

    public void InvalidateCache()
    {
    }
}

internal sealed class InMemoryChunkVectorIndexDouble : IChunkEmbeddingIndex
{
    private readonly List<VectorSearchHit> _hits = new();

    public int Count => _hits.Count;

    public void AddHit(string chapterId, int position, float score)
    {
        _hits.Add(new VectorSearchHit(ChunkKey.Format(chapterId, position), score));
    }

    public Task<bool> UpsertAsync(string key, float[] vector, CancellationToken ct = default)
    {
        _hits.Add(new VectorSearchHit(key, vector.Length));
        return Task.FromResult(true);
    }

    public Task<bool> UpsertBatchAsync(IReadOnlyList<(string Key, float[] Vector)> items, CancellationToken ct = default)
    {
        foreach (var (key, vector) in items)
            _hits.Add(new VectorSearchHit(key, vector.Length));
        return Task.FromResult(true);
    }

    public Task<bool> RemoveAsync(string key, CancellationToken ct = default)
    {
        _hits.RemoveAll(hit => string.Equals(hit.Key, key, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, int topK, CancellationToken ct = default)
    {
        IReadOnlyList<VectorSearchHit> hits = _hits
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult(hits);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_hits.Any(hit => string.Equals(hit.Key, key, StringComparison.OrdinalIgnoreCase)));

    public IReadOnlyCollection<string> GetAllKeys() =>
        _hits.Select(hit => hit.Key).ToArray();

    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

    public void InvalidateCache()
    {
    }

    public Task<IReadOnlyList<ChunkVectorEntry>> GetByChapterAsync(string chapterId, CancellationToken ct = default)
    {
        IReadOnlyList<ChunkVectorEntry> entries = _hits
            .Where(hit => ChunkKey.TryParse(hit.Key, out var hitChapterId, out _)
                && string.Equals(hitChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
            .Select(hit =>
            {
                ChunkKey.TryParse(hit.Key, out var hitChapterId, out var position);
                return new ChunkVectorEntry(hit.Key, hitChapterId, position, new[] { hit.Score });
            })
            .ToList();

        return Task.FromResult(entries);
    }

    public Task<int> RemoveByChapterAsync(string chapterId, CancellationToken ct = default)
    {
        var removed = _hits.RemoveAll(hit => ChunkKey.TryParse(hit.Key, out var hitChapterId, out _)
            && string.Equals(hitChapterId, chapterId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<VectorSearchHit>> SearchWithinChaptersAsync(
        float[] queryVector,
        IReadOnlySet<string> chapterIds,
        int topK,
        CancellationToken ct = default)
    {
        IReadOnlyList<VectorSearchHit> hits = _hits
            .Where(hit => ChunkKey.TryParse(hit.Key, out var chapterId, out _)
                && chapterIds.Contains(chapterId))
            .OrderByDescending(hit => hit.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult(hits);
    }
}

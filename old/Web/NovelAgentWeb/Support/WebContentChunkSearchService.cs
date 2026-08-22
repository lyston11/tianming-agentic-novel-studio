using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class WebContentChunkSearchService : IContentChunkSearchService
{
    private const string SourceType = "chapter";
    private const string DocumentRole = "chapter_body";
    private const int ChunkSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _userId;
    private readonly string _projectId;
    private readonly IVectorStore? _vectorStore;
    private readonly IMicroEmbeddingService? _embeddingService;

    public WebContentChunkSearchService(
        IServiceScopeFactory scopeFactory,
        string userId,
        string projectId,
        IVectorStore? vectorStore,
        IMicroEmbeddingService? embeddingService)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _userId = userId;
        _projectId = projectId;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
    }

    public async Task<List<ContentChunkHit>> SearchAsync(string query, int topK = 5)
    {
        if (topK <= 0 || string.IsNullOrWhiteSpace(query))
            return new List<ContentChunkHit>();

        if (_vectorStore != null && _embeddingService != null)
        {
            try
            {
                var queryVector = await _embeddingService.EncodeAsync(query, EmbeddingMode.Query)
                    .ConfigureAwait(false);
                var results = await _vectorStore.SearchSimilarAsync(
                        _userId,
                        queryVector,
                        topK,
                        new Dictionary<string, object>
                        {
                            ["project_id"] = _projectId,
                            ["source_type"] = SourceType
                        })
                    .ConfigureAwait(false);

                return results
                    .Where(r => string.Equals(r.ProjectId, _projectId, StringComparison.OrdinalIgnoreCase))
                    .Where(r => string.Equals(r.SourceType, SourceType, StringComparison.OrdinalIgnoreCase))
                    .Select(r => new ContentChunkHit(
                        FirstNonEmpty(r.ChapterId, r.SourceId),
                        r.ChunkIndex ?? 0,
                        r.Content ?? string.Empty,
                        r.Score))
                    .Where(h => !string.IsNullOrWhiteSpace(h.ChapterId) && !string.IsNullOrWhiteSpace(h.Content))
                    .ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TM.App.Log($"[WebContentChunkSearch] Qdrant search failed, falling back to SQLite text scan: {ex.Message}");
            }
        }

        return await SearchSqliteTextAsync(query, topK).ConfigureAwait(false);
    }

    public async Task<List<ContentChunkHit>> SearchByChapterAsync(string chapterId, int topK = 2)
    {
        if (string.IsNullOrWhiteSpace(chapterId) || topK <= 0)
            return new List<ContentChunkHit>();

        var chunks = await LoadChapterChunksAsync(chapterId, CancellationToken.None).ConfigureAwait(false);
        return chunks.Take(topK).ToList();
    }

    public Task InvalidateChapterAsync(string chapterId) => Task.CompletedTask;

    public async Task<List<ContentChunkHit>> SearchByChapterPositionAsync(
        string chapterId,
        int startPosition,
        int windowSize = 1,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chapterId) || startPosition < 0 || windowSize <= 0)
            return new List<ContentChunkHit>();

        var chunks = await LoadChapterChunksAsync(chapterId, ct).ConfigureAwait(false);
        return chunks
            .Where(c => c.Position >= startPosition)
            .Take(windowSize)
            .ToList();
    }

    public async Task<IReadOnlyList<ContentChunkHit>> GetChunksAsync(
        string chapterId,
        CancellationToken ct = default)
    {
        return await LoadChapterChunksAsync(chapterId, ct).ConfigureAwait(false);
    }

    public void InvalidateCache()
    {
    }

    private async Task<List<ContentChunkHit>> SearchSqliteTextAsync(string query, int topK)
    {
        var queryTerms = query.Split(new[] { ' ', '，', ',', '、' }, StringSplitOptions.RemoveEmptyEntries);
        if (queryTerms.Length == 0)
            return new List<ContentChunkHit>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
        var chapters = await db.Chapters
            .AsNoTracking()
            .Where(c => c.ProjectId == _projectId)
            .OrderBy(c => c.ChapterNumber)
            .Select(c => new { c.Id })
            .ToListAsync()
            .ConfigureAwait(false);

        var hits = new List<ContentChunkHit>();
        foreach (var chapter in chapters)
        {
            var chunks = await LoadChapterChunksAsync(chapter.Id, CancellationToken.None, contentDocuments)
                .ConfigureAwait(false);
            hits.AddRange(chunks
                .Select(chunk => chunk with { Score = CalculateRelevance(queryTerms, chunk.Content) })
                .Where(chunk => chunk.Score > 0));
        }

        return hits
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();
    }

    private async Task<List<ContentChunkHit>> LoadChapterChunksAsync(
        string chapterId,
        CancellationToken ct,
        IContentDocumentService? existingContentDocuments = null)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return new List<ContentChunkHit>();

        IContentDocumentService contentDocuments;
        IServiceScope? scope = null;
        if (existingContentDocuments != null)
        {
            contentDocuments = existingContentDocuments;
        }
        else
        {
            scope = _scopeFactory.CreateScope();
            contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
        }

        try
        {
            var text = await contentDocuments.GetTextAsync(
                    _userId,
                    _projectId,
                    SourceType,
                    chapterId,
                    DocumentRole,
                    ct)
                .ConfigureAwait(false);

            return ChunkContent(chapterId, text);
        }
        catch (KeyNotFoundException)
        {
            return new List<ContentChunkHit>();
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private static List<ContentChunkHit> ChunkContent(string chapterId, string content)
    {
        var chunks = new List<ContentChunkHit>();
        if (string.IsNullOrWhiteSpace(content))
            return chunks;

        for (var i = 0; i < content.Length; i += ChunkSize)
        {
            var length = Math.Min(ChunkSize, content.Length - i);
            var chunk = content.Substring(i, length).Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
                chunks.Add(new ContentChunkHit(chapterId, chunks.Count, chunk, 1.0));
        }

        return chunks;
    }

    private static double CalculateRelevance(string[] queryTerms, string content)
    {
        if (queryTerms.Length == 0 || string.IsNullOrWhiteSpace(content))
            return 0;

        var score = 0.0;
        foreach (var term in queryTerms)
        {
            if (content.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 1.0;
        }

        return score / queryTerms.Length;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }
}

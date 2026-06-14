using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Web.NovelAgentWeb.Services.Content;

public class ContentDocumentService : IContentDocumentService
{
    private const int MaxChunkLength = 4000;
    private static readonly TimeSpan HotTextTtl = TimeSpan.FromMinutes(10);

    private readonly NovelAgentDbContext _db;
    private readonly IDistributedCacheService? _redisCache;
    private readonly IMemoryCacheService? _memoryCache;
    private readonly ILogger<ContentDocumentService>? _logger;
    private readonly IAgentMemoryVersionService? _versions;

    public ContentDocumentService(
        NovelAgentDbContext db,
        IDistributedCacheService? redisCache = null,
        IMemoryCacheService? memoryCache = null,
        ILogger<ContentDocumentService>? logger = null,
        IAgentMemoryVersionService? versions = null)
    {
        _db = db;
        _redisCache = redisCache;
        _memoryCache = memoryCache;
        _logger = logger;
        _versions = versions;
    }

    public async Task<ContentDocument> SaveTextAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        string title,
        string content,
        CancellationToken ct = default)
    {
        var normalizedProjectId = NormalizeProjectId(projectId);
        var version = await GetNextVersionAsync(userId, normalizedProjectId, sourceType, sourceId, documentRole, ct).ConfigureAwait(false);
        return await SaveTextCoreAsync(
            userId,
            normalizedProjectId,
            sourceType,
            sourceId,
            documentRole,
            title,
            content,
            version,
            ct).ConfigureAwait(false);
    }

    private async Task<ContentDocument> SaveTextCoreAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        string title,
        string content,
        int version,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var document = new ContentDocument
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            SourceType = sourceType,
            SourceId = sourceId,
            DocumentRole = documentRole,
            Title = title,
            MimeType = "text/plain",
            ContentHash = Sha256(content),
            Version = version,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.ContentDocuments.Add(document);

        var chunks = SplitIntoChunks(content);
        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
        {
            var chunkRow = new ContentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = document.Id,
                ChunkIndex = index,
                ChunkText = chunk.Text,
                TokenCount = EstimateTokenCount(chunk.Text),
                CharStart = chunk.Start,
                CharEnd = chunk.End,
                ContentHash = Sha256(chunk.Text)
            };
            _db.ContentChunks.Add(chunkRow);

            _db.ContentVectorPoints.Add(new ContentVectorPoint
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = document.Id,
                ChunkId = chunkRow.Id,
                QdrantCollection = $"novel_agent_{userId}",
                QdrantPointId = $"content_{document.Id}_{index}",
                VectorModel = "pending",
                IndexStatus = "pending"
            });
        }

        await _db.SaveChangesAsync(ct);
        await RefreshHotTextCacheAsync(userId, document.ProjectId, sourceType, sourceId, documentRole, content, ct);
        await BumpContentVersionAsync(userId, document.ProjectId, ct).ConfigureAwait(false);
        return document;
    }

    public async Task<ContentDocument> SaveOrReplaceTextAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        string title,
        string content,
        CancellationToken ct = default)
    {
        var normalizedProjectId = NormalizeProjectId(projectId);
        var now = DateTime.UtcNow;
        var existing = await _db.ContentDocuments
            .Where(d =>
                d.UserId == userId &&
                d.ProjectId == normalizedProjectId &&
                d.SourceType == sourceType &&
                d.SourceId == sourceId &&
                d.DocumentRole == documentRole)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var nextVersion = existing.Count == 0 ? 1 : existing.Max(d => d.Version) + 1;
        foreach (var document in existing.Where(d => d.Status == "active"))
        {
            document.Status = "archived";
            document.UpdatedAt = now;
        }

        return await SaveTextCoreAsync(
            userId,
            normalizedProjectId,
            sourceType,
            sourceId,
            documentRole,
            title,
            content,
            nextVersion,
            ct).ConfigureAwait(false);
    }

    public async Task<string> GetTextAsync(
        string userId,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct = default) =>
        await GetTextCoreAsync(
            userId,
            projectId: null,
            projectScoped: false,
            sourceType,
            sourceId,
            documentRole,
            ct).ConfigureAwait(false);

    public async Task<string> GetTextAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct = default) =>
        await GetTextCoreAsync(
            userId,
            NormalizeProjectId(projectId),
            projectScoped: true,
            sourceType,
            sourceId,
            documentRole,
            ct).ConfigureAwait(false);

    private async Task<string> GetTextCoreAsync(
        string userId,
        string? projectId,
        bool projectScoped,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct)
    {
        var cacheKey = BuildHotTextCacheKey(userId, projectId, sourceType, sourceId, documentRole);
        if (projectScoped)
        {
            var memoryCached = _memoryCache?.Get<string>(cacheKey);
            if (memoryCached != null)
                return memoryCached;

            if (_redisCache != null)
            {
                var redisCached = await _redisCache.GetAsync<string>(cacheKey, ct).ConfigureAwait(false);
                if (redisCached != null)
                {
                    _memoryCache?.Set(cacheKey, redisCached, HotTextTtl);
                    return redisCached;
                }
            }
        }

        var query = _db.ContentDocuments
            .AsNoTracking()
            .Where(d =>
                d.UserId == userId &&
                d.SourceType == sourceType &&
                d.SourceId == sourceId &&
                d.DocumentRole == documentRole &&
                d.Status == "active");
        if (projectScoped)
            query = query.Where(d => d.ProjectId == projectId);

        var documents = await query
            .OrderByDescending(d => d.Version)
            .ThenByDescending(d => d.UpdatedAt)
            .Take(projectScoped ? 1 : 2)
            .ToListAsync(ct);

        if (documents.Count == 0)
            throw new KeyNotFoundException($"Content document not found for {sourceType}:{sourceId}:{documentRole}");
        if (!projectScoped && documents.Select(d => d.ProjectId).Distinct().Take(2).Count() > 1)
            throw new InvalidOperationException($"ProjectId is required for ambiguous content document {sourceType}:{sourceId}:{documentRole}");

        var document = documents[0];

        var chunks = await _db.ContentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == document.Id)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => c.ChunkText)
            .ToListAsync(ct);

        var content = string.Join(string.Empty, chunks);
        await RefreshHotTextCacheAsync(userId, document.ProjectId, sourceType, sourceId, documentRole, content, ct);
        return content;
    }

    public async Task DeleteBySourceAsync(
        string userId,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct = default) =>
        await DeleteBySourceCoreAsync(
            userId,
            projectId: null,
            projectScoped: false,
            sourceType,
            sourceId,
            documentRole,
            ct).ConfigureAwait(false);

    public async Task DeleteBySourceAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct = default) =>
        await DeleteBySourceCoreAsync(
            userId,
            NormalizeProjectId(projectId),
            projectScoped: true,
            sourceType,
            sourceId,
            documentRole,
            ct).ConfigureAwait(false);

    private async Task DeleteBySourceCoreAsync(
        string userId,
        string? projectId,
        bool projectScoped,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct)
    {
        var query = _db.ContentDocuments
            .Where(d =>
                d.UserId == userId &&
                d.SourceType == sourceType &&
                d.SourceId == sourceId &&
                d.DocumentRole == documentRole);
        if (projectScoped)
            query = query.Where(d => d.ProjectId == projectId);

        var documents = await query.ToListAsync(ct);

        if (documents.Count == 0)
            return;

        _db.ContentDocuments.RemoveRange(documents);
        await _db.SaveChangesAsync(ct);
        foreach (var cachedProjectId in documents.Select(d => d.ProjectId).Distinct())
            await RemoveHotTextCacheAsync(userId, cachedProjectId, sourceType, sourceId, documentRole, ct);
        await BumpContentVersionAsync(userId, documents.First().ProjectId, ct).ConfigureAwait(false);
    }

    private async Task RefreshHotTextCacheAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        string content,
        CancellationToken ct)
    {
        var cacheKey = BuildHotTextCacheKey(userId, projectId, sourceType, sourceId, documentRole);
        _memoryCache?.Set(cacheKey, content, HotTextTtl);

        if (_redisCache == null)
            return;

        try
        {
            await _redisCache.SetAsync(cacheKey, content, HotTextTtl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to refresh content text cache for {CacheKey}", cacheKey);
        }
    }

    private async Task RemoveHotTextCacheAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct)
    {
        var cacheKey = BuildHotTextCacheKey(userId, projectId, sourceType, sourceId, documentRole);
        _memoryCache?.Remove(cacheKey);

        if (_redisCache == null)
            return;

        try
        {
            await _redisCache.RemoveAsync(cacheKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to remove content text cache for {CacheKey}", cacheKey);
        }
    }

    private static string BuildHotTextCacheKey(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole) =>
        $"content:text:{userId}:{projectId ?? "global"}:{sourceType}:{sourceId}:{documentRole}";

    private Task BumpContentVersionAsync(string userId, string? projectId, CancellationToken ct) =>
        _versions == null
            ? Task.CompletedTask
            : _versions.BumpAsync(userId, string.IsNullOrWhiteSpace(projectId) ? null : projectId, null, "content", ct);

    private async Task<int> GetNextVersionAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct)
    {
        var currentMax = await _db.ContentDocuments
            .AsNoTracking()
            .Where(d =>
                d.UserId == userId &&
                d.ProjectId == projectId &&
                d.SourceType == sourceType &&
                d.SourceId == sourceId &&
                d.DocumentRole == documentRole)
            .Select(d => (int?)d.Version)
            .MaxAsync(ct)
            .ConfigureAwait(false);

        return (currentMax ?? 0) + 1;
    }

    private static string? NormalizeProjectId(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();

    private static IReadOnlyList<(string Text, int Start, int End)> SplitIntoChunks(string content)
    {
        var chunks = new List<(string Text, int Start, int End)>();
        var paragraphStart = 0;
        while (paragraphStart < content.Length)
        {
            var nextBreak = content.IndexOf("\n\n", paragraphStart, StringComparison.Ordinal);
            var paragraphEnd = nextBreak < 0 ? content.Length : nextBreak;
            AddParagraphChunks(content, paragraphStart, paragraphEnd, chunks);
            paragraphStart = nextBreak < 0 ? content.Length : nextBreak + 2;
        }

        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(content))
        {
            chunks.Add((content.Trim(), 0, content.Length));
        }

        return chunks;
    }

    private static void AddParagraphChunks(
        string content,
        int paragraphStart,
        int paragraphEnd,
        List<(string Text, int Start, int End)> chunks)
    {
        var start = paragraphStart;
        while (start < paragraphEnd)
        {
            var end = Math.Min(start + MaxChunkLength, paragraphEnd);
            var text = content[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                chunks.Add((text, start, end));
            }
            start = end;
        }
    }

    private static int EstimateTokenCount(string text) =>
        Math.Max(1, (int)Math.Ceiling(text.Length * 0.75));

    private static string Sha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

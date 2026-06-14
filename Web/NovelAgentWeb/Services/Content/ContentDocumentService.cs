using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Content;

public class ContentDocumentService : IContentDocumentService
{
    private const int MaxChunkSize = 4000;
    private const int ChunkOverlap = 200;

    private readonly NovelAgentDbContext _db;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<ContentDocumentService> _logger;

    public ContentDocumentService(
        NovelAgentDbContext db,
        IVectorStore vectorStore,
        ILogger<ContentDocumentService> logger)
    {
        _db = db;
        _vectorStore = vectorStore;
        _logger = logger;
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
        var contentHash = ComputeSha256(content);

        var existing = await _db.ContentDocuments
            .FirstOrDefaultAsync(d => d.SourceType == sourceType && d.SourceId == sourceId, ct);

        if (existing != null)
        {
            if (existing.ContentHash == contentHash)
                return existing;

            await DeleteDocumentAsync(existing.Id, ct);
        }

        var document = new ContentDocument
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = projectId,
            SourceType = sourceType,
            SourceId = sourceId,
            DocumentRole = documentRole,
            Title = title,
            MimeType = "text/plain",
            ContentHash = contentHash,
            Version = existing?.Version + 1 ?? 1,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ContentDocuments.Add(document);

        var chunks = SplitIntoChunks(content);
        for (int i = 0; i < chunks.Count; i++)
        {
            var (text, start, end) = chunks[i];
            var chunk = new ContentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = document.Id,
                ChunkIndex = i,
                ChunkText = text,
                TokenCount = EstimateTokenCount(text),
                CharStart = start,
                CharEnd = end,
                ContentHash = ComputeSha256(text)
            };
            _db.ContentChunks.Add(chunk);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Saved content document {DocumentId} for {SourceType}/{SourceId} with {ChunkCount} chunks",
            document.Id, sourceType, sourceId, chunks.Count);

        return document;
    }

    public async Task<string?> GetDocumentContentAsync(string documentId, CancellationToken ct = default)
    {
        var chunks = await _db.ContentChunks
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => c.ChunkText)
            .ToListAsync(ct);

        return chunks.Count == 0 ? null : string.Join("\n\n", chunks);
    }

    public async Task<string?> GetDocumentContentBySourceAsync(string sourceType, string sourceId, CancellationToken ct = default)
    {
        var document = await _db.ContentDocuments
            .FirstOrDefaultAsync(d => d.SourceType == sourceType && d.SourceId == sourceId && d.Status == "active", ct);

        if (document == null) return null;

        return await GetDocumentContentAsync(document.Id, ct);
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken ct = default)
    {
        var document = await _db.ContentDocuments
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document == null) return;

        await _vectorStore.DeleteVectorsByFilterAsync(
            document.UserId,
            new Dictionary<string, object>
            {
                ["source_type"] = document.SourceType,
                ["source_id"] = document.SourceId
            },
            ct);

        _db.ContentVectorPoints.RemoveRange(
            _db.ContentVectorPoints.Where(v => v.DocumentId == documentId));
        _db.ContentChunks.RemoveRange(document.Chunks);
        _db.ContentDocuments.Remove(document);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted content document {DocumentId}", documentId);
    }

    public async Task DeleteDocumentBySourceAsync(string sourceType, string sourceId, CancellationToken ct = default)
    {
        var document = await _db.ContentDocuments
            .FirstOrDefaultAsync(d => d.SourceType == sourceType && d.SourceId == sourceId, ct);

        if (document == null) return;

        await DeleteDocumentAsync(document.Id, ct);
    }

    public async Task<ContentDocument?> GetDocumentBySourceAsync(string sourceType, string sourceId, CancellationToken ct = default)
    {
        return await _db.ContentDocuments
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.SourceType == sourceType && d.SourceId == sourceId && d.Status == "active", ct);
    }

    private static IReadOnlyList<(string Text, int Start, int End)> SplitIntoChunks(string content)
    {
        var chunks = new List<(string Text, int Start, int End)>();
        var start = 0;

        while (start < content.Length)
        {
            var length = Math.Min(MaxChunkSize, content.Length - start);
            var end = start + length;

            if (end < content.Length)
            {
                var paragraphBreak = content.LastIndexOf("\n\n", end - 1, length, StringComparison.Ordinal);
                if (paragraphBreak > start + 500)
                {
                    end = paragraphBreak + 2;
                }
            }

            var text = content[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                chunks.Add((text, start, end));
            }

            start = end > start ? end - ChunkOverlap : end;
            if (start >= content.Length) break;
        }

        return chunks;
    }

    private static int EstimateTokenCount(string text)
    {
        return (int)(text.Length * 0.75);
    }

    private static string ComputeSha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

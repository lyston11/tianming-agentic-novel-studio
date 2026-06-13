using System.Security.Cryptography;
using System.Text;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Content;

public class ContentDocumentService : IContentDocumentService
{
    private const int MaxChunkLength = 4000;

    private readonly NovelAgentDbContext _db;

    public ContentDocumentService(NovelAgentDbContext db)
    {
        _db = db;
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
            Version = 1,
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
        return document;
    }

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

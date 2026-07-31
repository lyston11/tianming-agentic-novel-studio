using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Rag;

public interface IMultiScaleVectorIndexer
{
    Task<int> IndexDocumentAsync(
        string userId,
        string documentBlobId,
        CancellationToken cancellationToken = default);
}

public sealed class MultiScaleVectorIndexer : IMultiScaleVectorIndexer
{
    private readonly NovelAgentDbContext _db;
    private readonly IVectorStore _vectors;
    private readonly IMicroEmbeddingService _embedding;
    private readonly string _embeddingVersion;
    private readonly ILogger<MultiScaleVectorIndexer> _logger;

    public MultiScaleVectorIndexer(
        NovelAgentDbContext db,
        IVectorStore vectors,
        IMicroEmbeddingService embedding,
        IConfiguration configuration,
        ILogger<MultiScaleVectorIndexer> logger)
    {
        _db = db;
        _vectors = vectors;
        _embedding = embedding;
        _embeddingVersion = configuration["Embedding:Model"]
            ?? throw new InvalidOperationException("Embedding:Model 未配置，无法生成可重建索引版本。");
        _logger = logger;
    }

    public async Task<int> IndexDocumentAsync(
        string userId,
        string documentBlobId,
        CancellationToken cancellationToken = default)
    {
        var blob = await _db.KnowledgeDocumentBlobs.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == documentBlobId && item.UserId == userId && item.Status == "processed",
            cancellationToken) ?? throw new KeyNotFoundException("已处理知识文档不存在或不属于当前用户。");
        var sections = await _db.KnowledgeSections.AsNoTracking()
            .Where(item => item.UserId == userId && item.DocumentBlobId == blob.Id)
            .OrderBy(item => item.SectionIndex)
            .ToListAsync(cancellationToken);
        var chunks = await _db.KnowledgeChunks.AsNoTracking()
            .Where(item => item.UserId == userId && item.DocumentBlobId == blob.Id)
            .OrderBy(item => item.ChunkIndex)
            .ToListAsync(cancellationToken);
        var entries = await _db.KnowledgeEntries.AsNoTracking()
            .Where(item => item.UserId == userId && item.DocumentBlobId == blob.Id && item.Status == "active")
            .OrderBy(item => item.SourceEntryIndex)
            .ToListAsync(cancellationToken);
        var styles = await _db.StyleProfiles.AsNoTracking()
            .Where(item => item.UserId == userId && item.DocumentBlobId == blob.Id && item.Status == "active")
            .OrderBy(item => item.Version)
            .ToListAsync(cancellationToken);
        var sources = BuildSources(blob, sections, chunks, entries, styles);
        if (sources.Count == 0)
            return 0;

        await _vectors.InitializeUserCollectionAsync(userId, cancellationToken);
        await _vectors.DeleteVectorsByFilterAsync(userId, new Dictionary<string, object>
        {
            ["document_blob_id"] = blob.Id
        }, cancellationToken);
        var oldRecords = await _db.VectorIndexRecords.Where(item =>
            item.UserId == userId && item.DocumentBlobId == blob.Id).ToListAsync(cancellationToken);
        _db.VectorIndexRecords.RemoveRange(oldRecords);
        await _db.SaveChangesAsync(cancellationToken);

        var embeddings = await _embedding.EncodeBatchAsync(
            sources.Select(source => source.EmbeddingText).ToArray(),
            EmbeddingMode.Passage,
            cancellationToken);
        if (embeddings.Length != sources.Count)
            throw new InvalidOperationException("Embedding 批量结果数量与多尺度 source 数量不一致。");
        var now = DateTime.UtcNow;
        var collection = QdrantVectorStore.GetCollectionName(userId);
        var records = new List<VectorIndexRecord>(sources.Count);
        var vectorData = new List<VectorData>(sources.Count);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var pointId = DeterministicPointId(
                userId,
                source.SourceType,
                source.SourceId,
                blob.KnowledgeVersion,
                _embeddingVersion);
            var metadata = new Dictionary<string, object>
            {
                ["branch_id"] = source.BranchId,
                ["knowledge_version"] = blob.KnowledgeVersion,
                ["content_hash"] = source.ContentHash,
                ["embedding_version"] = _embeddingVersion,
                ["document_blob_id"] = blob.Id,
                ["parent_id"] = source.ParentId,
                ["previous_id"] = source.PreviousId,
                ["next_id"] = source.NextId
            };
            vectorData.Add(new VectorData
            {
                Id = pointId,
                Vector = embeddings[index],
                UserId = userId,
                ProjectId = blob.ProjectId,
                SourceType = source.SourceType,
                SourceId = source.SourceId,
                ChunkIndex = source.ChunkIndex,
                Content = null,
                Metadata = metadata
            });
            records.Add(new VectorIndexRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ProjectId = blob.ProjectId,
                BranchId = source.BranchId,
                DocumentBlobId = blob.Id,
                SourceDocumentId = blob.Id,
                SourceType = source.SourceType,
                SourceId = source.SourceId,
                ChunkIndex = source.ChunkIndex,
                KnowledgeVersion = blob.KnowledgeVersion,
                ContentHash = source.ContentHash,
                EmbeddingVersion = _embeddingVersion,
                QdrantCollection = collection,
                QdrantPointId = pointId,
                Status = "pending",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        _db.VectorIndexRecords.AddRange(records);
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            await _vectors.UpsertVectorsAsync(userId, vectorData, cancellationToken);
            foreach (var record in records)
            {
                record.Status = "completed";
                record.IndexedAt = DateTime.UtcNow;
                record.UpdatedAt = record.IndexedAt.Value;
            }
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Indexed knowledge document {DocumentBlobId} for user {UserId} at {ScaleCount} scales",
                blob.Id,
                userId,
                records.Count);
            return records.Count;
        }
        catch (Exception exception)
        {
            foreach (var record in records)
            {
                record.Status = "failed";
                record.ErrorMessage = exception.Message;
                record.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static List<VectorSource> BuildSources(
        KnowledgeDocumentBlob blob,
        IReadOnlyList<KnowledgeSection> sections,
        IReadOnlyList<KnowledgeChunk> chunks,
        IReadOnlyList<KnowledgeEntry> entries,
        IReadOnlyList<StyleProfile> styles)
    {
        var result = new List<VectorSource>();
        var documentSummary = entries.Select(entry => entry.Summary)
            .FirstOrDefault(summary => !string.IsNullOrWhiteSpace(summary))
            ?? string.Join("\n", sections.Select(section => section.Summary));
        if (!string.IsNullOrWhiteSpace(documentSummary))
        {
            result.Add(new VectorSource(
                "knowledge_document",
                blob.Id,
                documentSummary,
                blob.ContentHash,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null));
        }
        result.AddRange(sections.Select(section => new VectorSource(
            "knowledge_section",
            section.Id,
            $"{section.Title}\n{section.Summary}",
            Sha256(section.Text),
            string.Empty,
            blob.Id,
            string.Empty,
            string.Empty,
            section.SectionIndex)));
        result.AddRange(chunks.Select(chunk => new VectorSource(
            "knowledge_chunk",
            chunk.Id,
            chunk.Text,
            chunk.ContentHash,
            string.Empty,
            chunk.SectionId,
            chunk.PreviousChunkId ?? string.Empty,
            chunk.NextChunkId ?? string.Empty,
            chunk.ChunkIndex)));
        result.AddRange(entries.Select(entry => new VectorSource(
            "knowledge_entry",
            entry.Id,
            $"{entry.Title}\n{entry.Content}",
            Sha256(entry.Content),
            string.Empty,
            blob.Id,
            string.Empty,
            string.Empty,
            entry.SourceEntryIndex)));
        result.AddRange(styles.Select(style => new VectorSource(
            "style_profile",
            style.Id,
            style.FeaturesJson,
            Sha256(style.FeaturesJson),
            string.Empty,
            blob.Id,
            string.Empty,
            string.Empty,
            style.Version)));
        return result;
    }

    internal static string DeterministicPointId(params object[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        var guidBytes = bytes[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes).ToString();
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private sealed record VectorSource(
        string SourceType,
        string SourceId,
        string EmbeddingText,
        string ContentHash,
        string BranchId,
        string ParentId,
        string PreviousId,
        string NextId,
        int? ChunkIndex);
}

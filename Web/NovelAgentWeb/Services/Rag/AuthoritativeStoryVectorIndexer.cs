using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Rag;

public interface IAuthoritativeStoryVectorIndexer
{
    Task<int> IndexUserAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class AuthoritativeStoryVectorIndexer : IAuthoritativeStoryVectorIndexer
{
    private readonly NovelAgentDbContext _db;
    private readonly IVectorStore _vectors;
    private readonly IMicroEmbeddingService _embedding;
    private readonly string _embeddingVersion;
    private readonly ILogger<AuthoritativeStoryVectorIndexer> _logger;

    public AuthoritativeStoryVectorIndexer(
        NovelAgentDbContext db,
        IVectorStore vectors,
        IMicroEmbeddingService embedding,
        IConfiguration configuration,
        ILogger<AuthoritativeStoryVectorIndexer> logger)
    {
        _db = db;
        _vectors = vectors;
        _embedding = embedding;
        _embeddingVersion = configuration["Embedding:Model"]
            ?? throw new InvalidOperationException("Embedding:Model 未配置，无法生成故事索引版本。");
        _logger = logger;
    }

    public async Task<int> IndexUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var sources = await LoadSourcesAsync(userId, cancellationToken);
        if (sources.Count == 0)
            return 0;

        var embeddings = await _embedding.EncodeBatchAsync(
            sources.Select(source => source.EmbeddingText).ToArray(),
            EmbeddingMode.Passage,
            cancellationToken);
        if (embeddings.Length != sources.Count)
            throw new InvalidOperationException("Embedding 批量结果数量与故事 source 数量不一致。");

        var now = DateTime.UtcNow;
        var collection = QdrantVectorStore.GetCollectionName(userId);
        var records = new List<VectorIndexRecord>(sources.Count);
        var payloads = new List<VectorData>(sources.Count);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var pointId = MultiScaleVectorIndexer.DeterministicPointId(
                userId,
                source.SourceType,
                source.SourceDocumentId,
                source.SourceId,
                source.ChunkIndex ?? -1,
                source.ContentHash,
                _embeddingVersion);
            var metadata = new Dictionary<string, object>(source.Metadata, StringComparer.Ordinal)
            {
                ["branch_id"] = source.BranchId,
                ["content_hash"] = source.ContentHash,
                ["embedding_version"] = _embeddingVersion,
                ["source_document_id"] = source.SourceDocumentId,
                ["created_at_unix"] = new DateTimeOffset(source.CreatedAt).ToUnixTimeSeconds()
            };
            payloads.Add(new VectorData
            {
                Id = pointId,
                Vector = embeddings[index],
                UserId = userId,
                ProjectId = source.ProjectId,
                SourceType = source.SourceType,
                SourceId = source.SourceId,
                ChapterId = source.SourceType == "chapter" ? source.SourceId : null,
                ChunkIndex = source.ChunkIndex,
                Content = null,
                Metadata = metadata
            });
            records.Add(new VectorIndexRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ProjectId = source.ProjectId,
                BranchId = source.BranchId,
                DocumentBlobId = null,
                SourceDocumentId = source.SourceDocumentId,
                SourceType = source.SourceType,
                SourceId = source.SourceId,
                ChunkIndex = source.ChunkIndex,
                KnowledgeVersion = 0,
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
            await _vectors.UpsertVectorsAsync(userId, payloads, cancellationToken);
            foreach (var record in records)
            {
                record.Status = "completed";
                record.IndexedAt = DateTime.UtcNow;
                record.UpdatedAt = record.IndexedAt.Value;
            }
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Indexed {Count} authoritative story vectors for user {UserId}",
                records.Count,
                userId);
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
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<List<StoryVectorSource>> LoadSourcesAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var result = new List<StoryVectorSource>();
        var documents = await _db.ContentDocuments.AsNoTracking()
            .Where(document =>
                document.UserId == userId &&
                document.SourceType == "chapter" &&
                document.DocumentRole == "chapter_body" &&
                (document.Status == "active" || document.Status == "history"))
            .OrderBy(document => document.ProjectId)
            .ThenBy(document => document.SourceId)
            .ThenBy(document => document.Version)
            .ToListAsync(cancellationToken);
        var documentIds = documents.Select(document => document.Id).ToArray();
        var chunks = await _db.ContentChunks.AsNoTracking()
            .Where(chunk => documentIds.Contains(chunk.DocumentId))
            .OrderBy(chunk => chunk.DocumentId)
            .ThenBy(chunk => chunk.ChunkIndex)
            .ToListAsync(cancellationToken);
        var documentsById = documents.ToDictionary(document => document.Id, StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            var document = documentsById[chunk.DocumentId];
            if (string.IsNullOrWhiteSpace(document.ProjectId) || string.IsNullOrWhiteSpace(chunk.ChunkText))
                continue;
            result.Add(new StoryVectorSource(
                document.ProjectId,
                string.Empty,
                "chapter",
                document.SourceId,
                document.Id,
                chunk.ChunkIndex,
                chunk.ChunkText,
                chunk.ContentHash,
                document.CreatedAt,
                new Dictionary<string, object>
                {
                    ["content_document_id"] = document.Id,
                    ["document_version"] = document.Version,
                    ["document_status"] = document.Status
                }));
        }

        var summaries = await _db.ContinuitySummaries.AsNoTracking()
            .Where(summary => summary.UserId == userId && summary.Status == "committed")
            .OrderBy(summary => summary.CreatedAt)
            .ToListAsync(cancellationToken);
        result.AddRange(summaries
            .Where(summary => !string.IsNullOrWhiteSpace(summary.SummaryJson))
            .Select(summary => new StoryVectorSource(
                summary.ProjectId,
                summary.BranchId ?? string.Empty,
                "continuity_summary",
                summary.Id,
                summary.Id,
                null,
                summary.SummaryJson,
                Sha256(summary.SummaryJson),
                summary.CreatedAt,
                new Dictionary<string, object>
                {
                    ["chapter_id"] = summary.ChapterId,
                    ["chapter_version_id"] = summary.ChapterVersionId
                })));

        var changes = await _db.CanonChanges.AsNoTracking()
            .Where(change => change.UserId == userId && change.Status == "committed")
            .OrderBy(change => change.CreatedAt)
            .ToListAsync(cancellationToken);
        result.AddRange(changes.Select(change =>
        {
            var text = $"{change.Subject}\n{change.ChangeJson}";
            return new StoryVectorSource(
                change.ProjectId,
                change.BranchId ?? string.Empty,
                "canon_change",
                change.Id,
                change.Id,
                null,
                text,
                Sha256(text),
                change.CreatedAt,
                new Dictionary<string, object>
                {
                    ["chapter_id"] = change.ChapterId,
                    ["chapter_version_id"] = change.ChapterVersionId,
                    ["change_type"] = change.ChangeType,
                    ["subject"] = change.Subject
                });
        }));
        return result;
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private sealed record StoryVectorSource(
        string ProjectId,
        string BranchId,
        string SourceType,
        string SourceId,
        string SourceDocumentId,
        int? ChunkIndex,
        string EmbeddingText,
        string ContentHash,
        DateTime CreatedAt,
        IReadOnlyDictionary<string, object> Metadata);
}

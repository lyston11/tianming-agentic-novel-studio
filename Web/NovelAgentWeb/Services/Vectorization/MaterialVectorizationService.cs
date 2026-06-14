using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Vectorization;

public sealed class MaterialVectorizationService : IMaterialVectorizationService
{
    private readonly NovelAgentDbContext _db;
    private readonly IVectorStore _vectorStore;
    private readonly IMicroEmbeddingService _embedding;
    private readonly IMaterialChunker _chunker;
    private readonly IQdrantCollectionManager _collectionManager;
    private readonly IContentDocumentService _contentDocuments;
    private readonly ILogger<MaterialVectorizationService> _logger;

    public MaterialVectorizationService(
        NovelAgentDbContext db,
        IVectorStore vectorStore,
        IMicroEmbeddingService embedding,
        IMaterialChunker chunker,
        IQdrantCollectionManager collectionManager,
        IContentDocumentService contentDocuments,
        ILogger<MaterialVectorizationService> logger)
    {
        _db = db;
        _vectorStore = vectorStore;
        _embedding = embedding;
        _chunker = chunker;
        _collectionManager = collectionManager;
        _contentDocuments = contentDocuments;
        _logger = logger;
    }

    public async Task VectorizeMaterialAsync(string materialId, string userId, CancellationToken ct = default)
    {
        var material = await _db.Materials
            .FirstOrDefaultAsync(m => m.Id == materialId && m.UserId == userId, ct);

        if (material == null)
            throw new KeyNotFoundException($"Material {materialId} not found");

        await _collectionManager.EnsureUserCollectionAsync(userId, ct);

        var document = await _db.ContentDocuments
            .AsNoTracking()
            .Where(d =>
                d.UserId == userId &&
                d.SourceType == "material" &&
                d.SourceId == materialId &&
                d.DocumentRole == "material_raw" &&
                d.Status == "active")
            .OrderByDescending(d => d.Version)
            .ThenByDescending(d => d.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var content = await _contentDocuments.GetTextAsync(userId, material.ProjectId, "material", materialId, "material_raw", ct);
        var chunks = await LoadVectorChunksAsync(document, content, materialId, ct);
        var pointRows = document == null
            ? new Dictionary<string, ContentVectorPoint>()
            : await _db.ContentVectorPoints
                .Where(p => p.DocumentId == document.Id && p.ChunkId != null)
                .ToDictionaryAsync(p => p.ChunkId!, ct);

        // Delete existing vectors for this material
        await _vectorStore.DeleteVectorsByFilterAsync(userId, new Dictionary<string, object>
        {
            ["source_type"] = "material",
            ["source_id"] = materialId
        }, ct);

        // Build vectors
        var vectors = new List<VectorData>();
        var pointBindings = new List<(ContentVectorPoint Point, string VectorId)>();
        foreach (var chunk in chunks)
        {
            var embedding = await _embedding.EncodeAsync(chunk.Content, EmbeddingMode.Passage, ct);
            var point = chunk.ContentChunkId != null && pointRows.TryGetValue(chunk.ContentChunkId, out var matchedPoint)
                ? matchedPoint
                : null;
            var vectorId = EnsureUuidPointId(point?.QdrantPointId);
            vectors.Add(new VectorData
            {
                Id = vectorId,
                Vector = embedding,
                UserId = userId,
                ProjectId = material.ProjectId ?? "",
                SourceType = "material",
                SourceId = materialId,
                ChunkIndex = chunk.ChunkIndex,
                Content = chunk.Content,
            });
            if (point != null)
                pointBindings.Add((point, vectorId));
        }

        try
        {
            await _vectorStore.UpsertVectorsAsync(userId, vectors, ct);
        }
        catch (Exception ex)
        {
            await MarkVectorPointsFailedAsync(pointBindings.Select(x => x.Point), ex.Message, ct);
            throw;
        }

        MarkVectorPointsCompleted(userId, pointBindings);

        material.VectorChunkCount = chunks.Count;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Vectorized material {MaterialId} with {Count} chunks", materialId, chunks.Count);
    }

    public async Task<int> VectorizeAllMaterialsAsync(string projectId, string userId, CancellationToken ct = default)
    {
        var materials = await _db.Materials
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .ToListAsync(ct);

        int success = 0;
        foreach (var material in materials)
        {
            try
            {
                await VectorizeMaterialAsync(material.Id, userId, ct);
                success++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to vectorize material {Id}", material.Id);
            }
        }
        return success;
    }

    private async Task<List<VectorChunkSource>> LoadVectorChunksAsync(
        ContentDocument? document,
        string content,
        string materialId,
        CancellationToken ct)
    {
        if (document != null)
        {
            var contentChunks = await _db.ContentChunks
                .AsNoTracking()
                .Where(c => c.DocumentId == document.Id)
                .OrderBy(c => c.ChunkIndex)
                .ToListAsync(ct);

            if (contentChunks.Count > 0)
            {
                return contentChunks.Select(c => new VectorChunkSource(
                    c.ChunkIndex,
                    c.ChunkText,
                    c.Id)).ToList();
            }
        }

        return _chunker.ChunkText(content, materialId)
            .Select(c => new VectorChunkSource(c.ChunkIndex, c.Content, null))
            .ToList();
    }

    private void MarkVectorPointsCompleted(
        string userId,
        IReadOnlyList<(ContentVectorPoint Point, string VectorId)> bindings)
    {
        var indexedAt = DateTime.UtcNow;
        foreach (var (point, vectorId) in bindings)
        {
            point.QdrantCollection = QdrantVectorStore.GetCollectionName(userId);
            point.QdrantPointId = vectorId;
            point.VectorModel = _embedding.GetType().Name;
            point.IndexStatus = "completed";
            point.IndexedAt = indexedAt;
            point.ErrorMessage = null;
        }
    }

    private async Task MarkVectorPointsFailedAsync(
        IEnumerable<ContentVectorPoint> points,
        string errorMessage,
        CancellationToken ct)
    {
        foreach (var point in points)
        {
            point.VectorModel = _embedding.GetType().Name;
            point.IndexStatus = "failed";
            point.ErrorMessage = errorMessage;
            point.IndexedAt = null;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string EnsureUuidPointId(string? existing) =>
        Guid.TryParse(existing, out _) ? existing : Guid.NewGuid().ToString();

    private sealed record VectorChunkSource(int ChunkIndex, string Content, string? ContentChunkId);
}

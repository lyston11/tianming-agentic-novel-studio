using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Vectorization;

public sealed class MaterialVectorizationService : IMaterialVectorizationService
{
    private readonly NovelAgentDbContext _db;
    private readonly IVectorStore _vectorStore;
    private readonly IMicroEmbeddingService _embedding;
    private readonly IMaterialChunker _chunker;
    private readonly IQdrantCollectionManager _collectionManager;
    private readonly ILogger<MaterialVectorizationService> _logger;

    public MaterialVectorizationService(
        NovelAgentDbContext db,
        IVectorStore vectorStore,
        IMicroEmbeddingService embedding,
        IMaterialChunker chunker,
        IQdrantCollectionManager collectionManager,
        ILogger<MaterialVectorizationService> logger)
    {
        _db = db;
        _vectorStore = vectorStore;
        _embedding = embedding;
        _chunker = chunker;
        _collectionManager = collectionManager;
        _logger = logger;
    }

    public async Task VectorizeMaterialAsync(string materialId, string userId, CancellationToken ct = default)
    {
        var material = await _db.Materials
            .FirstOrDefaultAsync(m => m.Id == materialId && m.UserId == userId, ct);

        if (material == null)
            throw new KeyNotFoundException($"Material {materialId} not found");

        await _collectionManager.EnsureUserCollectionAsync(userId, ct);

        // Read content
        string content;
        if (!string.IsNullOrEmpty(material.FilePath))
            content = await File.ReadAllTextAsync(material.FilePath, ct);
        else if (!string.IsNullOrEmpty(material.Content))
            content = material.Content;
        else
            throw new InvalidOperationException($"Material {materialId} has no content");

        // Chunk
        var chunks = _chunker.ChunkText(content, materialId);

        // Delete existing vectors for this material
        await _vectorStore.DeleteVectorsByFilterAsync(userId, new Dictionary<string, object>
        {
            ["source_type"] = "material",
            ["source_id"] = materialId
        }, ct);

        // Build vectors
        var vectors = new List<VectorData>();
        foreach (var chunk in chunks)
        {
            var embedding = await _embedding.EncodeAsync(chunk.Content, EmbeddingMode.Passage, ct);
            vectors.Add(new VectorData
            {
                Id = Guid.NewGuid().ToString(),
                Vector = embedding,
                UserId = userId,
                ProjectId = material.ProjectId ?? "",
                SourceType = "material",
                SourceId = materialId,
                ChunkIndex = chunk.ChunkIndex,
                Content = chunk.Content,
            });
        }

        await _vectorStore.UpsertVectorsAsync(userId, vectors, ct);

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
}

using Microsoft.EntityFrameworkCore;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Vectorization;

public sealed class MaterialVectorizationService : IMaterialVectorizationService
{
    private readonly NovelAgentDbContext _db;
    private readonly QdrantClient _qdrant;
    private readonly IMicroEmbeddingService _embedding;
    private readonly IMaterialChunker _chunker;
    private readonly IQdrantCollectionManager _collectionManager;
    private readonly ILogger<MaterialVectorizationService> _logger;

    public MaterialVectorizationService(
        NovelAgentDbContext db,
        QdrantClient qdrant,
        IMicroEmbeddingService embedding,
        IMaterialChunker chunker,
        IQdrantCollectionManager collectionManager,
        ILogger<MaterialVectorizationService> logger)
    {
        _db = db;
        _qdrant = qdrant;
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
        {
            throw new KeyNotFoundException($"Material {materialId} not found");
        }

        try
        {
            // Ensure collection exists
            await _collectionManager.EnsureUserCollectionAsync(userId, ct);

            // Read material content from FilePath or Content column
            string content;
            if (!string.IsNullOrEmpty(material.FilePath))
            {
                try
                {
                    content = await File.ReadAllTextAsync(material.FilePath, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read file {FilePath} for material {MaterialId}", material.FilePath, materialId);
                    throw;
                }
            }
            else if (!string.IsNullOrEmpty(material.Content))
            {
                content = material.Content;
            }
            else
            {
                throw new InvalidOperationException($"Material {materialId} has neither FilePath nor Content");
            }

            // Chunk material
            var chunks = _chunker.ChunkText(content, materialId);
            _logger.LogInformation("Material {MaterialId} chunked into {ChunkCount} pieces", materialId, chunks.Count);

            var collectionName = $"novel_agent_{userId}";

            // Delete existing vectors for this material to avoid duplicates
            try
            {
                await _qdrant.DeleteAsync(
                    collectionName,
                    new Filter
                    {
                        Must =
                        {
                            new Condition
                            {
                                Field = new FieldCondition
                                {
                                    Key = "entity_id",
                                    Match = new Match { Keyword = materialId }
                                }
                            }
                        }
                    },
                    cancellationToken: ct
                );
                _logger.LogInformation("Deleted existing vectors for material {MaterialId}", materialId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete existing vectors for material {MaterialId}, continuing with upsert", materialId);
            }

            // Generate embeddings and upsert to Qdrant
            var points = new List<PointStruct>();

            foreach (var chunk in chunks)
            {
                var embedding = await _embedding.EncodeAsync(chunk.Content, EmbeddingMode.Passage, ct);

                var point = new PointStruct
                {
                    Id = new PointId { Uuid = Guid.NewGuid().ToString() },
                    Vectors = embedding,
                    Payload =
                    {
                        ["user_id"] = userId,
                        ["project_id"] = material.ProjectId ?? "",
                        ["entity_type"] = "material",
                        ["entity_id"] = materialId,
                        ["chunk_id"] = chunk.ChunkId,
                        ["chunk_index"] = chunk.ChunkIndex,
                        ["chunk_total"] = chunk.ChunkTotal,
                        ["content"] = chunk.Content,
                        ["title"] = material.Title,
                        ["category"] = material.Category ?? "",
                        ["created_at"] = new DateTimeOffset(material.CreatedAt).ToUnixTimeSeconds()
                    }
                };

                points.Add(point);
            }

            await _qdrant.UpsertAsync(collectionName, points, cancellationToken: ct);

            // Update material vector count
            material.VectorChunkCount = chunks.Count;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Vectorized material {MaterialId} with {ChunkCount} chunks", materialId, chunks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to vectorize material {MaterialId}", materialId);
            throw;
        }
    }

    public async Task<int> VectorizeAllMaterialsAsync(string projectId, string userId, CancellationToken ct = default)
    {
        var materials = await _db.Materials
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .ToListAsync(ct);

        int successCount = 0;
        int failureCount = 0;

        _logger.LogInformation("Starting batch vectorization of {TotalCount} materials for project {ProjectId}", materials.Count, projectId);

        foreach (var material in materials)
        {
            try
            {
                await VectorizeMaterialAsync(material.Id, userId, ct);
                successCount++;
                _logger.LogInformation("Progress: {SuccessCount}/{TotalCount} materials vectorized successfully", successCount, materials.Count);
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogError(ex, "Failed to vectorize material {MaterialId} (title: {Title}). Continuing with remaining materials.", material.Id, material.Title);
            }
        }

        _logger.LogInformation("Batch vectorization completed: {SuccessCount} succeeded, {FailureCount} failed out of {TotalCount} materials", successCount, failureCount, materials.Count);

        return successCount;
    }
}

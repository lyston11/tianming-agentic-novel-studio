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

        // Ensure collection exists
        await _collectionManager.EnsureUserCollectionAsync(userId, ct);

        // Read material content
        var content = await File.ReadAllTextAsync(material.FilePath ?? "", ct);

        // Chunk material
        var chunks = _chunker.ChunkText(content, materialId);
        _logger.LogInformation("Material {MaterialId} chunked into {ChunkCount} pieces", materialId, chunks.Count);

        // Generate embeddings and upsert to Qdrant
        var collectionName = $"novel_agent_{userId}";
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

    public async Task<int> VectorizeAllMaterialsAsync(string projectId, string userId, CancellationToken ct = default)
    {
        var materials = await _db.Materials
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .ToListAsync(ct);

        int count = 0;
        foreach (var material in materials)
        {
            await VectorizeMaterialAsync(material.Id, userId, ct);
            count++;
        }

        return count;
    }
}

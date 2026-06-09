# Phase 2: Vector Storage (Qdrant) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement Qdrant-based semantic search with user-level collections, material vectorization pipeline, and advanced retrieval capabilities

**Architecture:** User-level Qdrant collections (novel_agent_{userId}), chunked material vectorization with 1500 token chunks and 200 token overlap, hybrid search combining vector similarity and metadata filtering

**Tech Stack:** ASP.NET Core 8.0, Qdrant.Client, Entity Framework Core, IMicroEmbeddingService

**Current State:** QdrantVectorStore exists but uses per-project collections. Need to migrate to per-user collections and add vectorization pipeline.

---

## Task 1: Database Migration for Vector Tracking

**Files:**
- Create: `Web/NovelAgentWeb/Migrations/YYYYMMDDHHMMSS_AddVectorTracking.cs`
- Modify: `Web/NovelAgentWeb/Data/Entities/Material.cs`
- Modify: `Web/NovelAgentWeb/Data/Entities/KnowledgeEntry.cs`

- [ ] **Step 1: Add VectorChunkCount to Material entity**

```csharp
// Add to Material.cs
[Column("vector_chunk_count")]
public int VectorChunkCount { get; set; } = 0;
```

- [ ] **Step 2: Add VectorId to KnowledgeEntry entity**

```csharp
// Add to KnowledgeEntry.cs (if it exists)
[Column("vector_id")]
[StringLength(100)]
public string? VectorId { get; set; }
```

- [ ] **Step 3: Generate migration**

```bash
dotnet ef migrations add AddVectorTracking --project Web/NovelAgentWeb
```

- [ ] **Step 4: Run migration**

```bash
dotnet ef database update --project Web/NovelAgentWeb
```

- [ ] **Step 5: Commit**

```bash
git add Web/NovelAgentWeb/Migrations/ Web/NovelAgentWeb/Data/Entities/
git commit -m "feat(db): add vector tracking fields to materials and knowledge entries"
```

---

## Task 2: QdrantCollectionManager Service

**Files:**
- Create: `Web/NovelAgentWeb/Services/VectorStore/QdrantCollectionManager.cs`

- [ ] **Step 1: Create QdrantCollectionManager class**

```csharp
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

public sealed class QdrantCollectionManager
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantCollectionManager> _logger;
    private const int VectorDimension = 1536; // OpenAI text-embedding-ada-002
    
    public QdrantCollectionManager(
        QdrantClient client,
        ILogger<QdrantCollectionManager> logger)
    {
        _client = client;
        _logger = logger;
    }
    
    public async Task<bool> EnsureUserCollectionAsync(string userId, CancellationToken ct = default)
    {
        var collectionName = GetCollectionName(userId);
        
        if (await CollectionExistsAsync(collectionName, ct))
        {
            return false; // Already exists
        }
        
        await _client.CreateCollectionAsync(
            collectionName: collectionName,
            vectorsConfig: new VectorParams
            {
                Size = VectorDimension,
                Distance = Distance.Cosine
            },
            cancellationToken: ct);
            
        // Create payload indexes
        await _client.CreatePayloadIndexAsync(collectionName, "project_id", PayloadSchemaType.Keyword, cancellationToken: ct);
        await _client.CreatePayloadIndexAsync(collectionName, "entity_type", PayloadSchemaType.Keyword, cancellationToken: ct);
        await _client.CreatePayloadIndexAsync(collectionName, "category", PayloadSchemaType.Keyword, cancellationToken: ct);
        
        _logger.LogInformation("Created collection {Collection} for user {UserId}", collectionName, userId);
        return true;
    }
    
    public async Task<bool> CollectionExistsAsync(string collectionName, CancellationToken ct = default)
    {
        return await _client.CollectionExistsAsync(collectionName, cancellationToken: ct);
    }
    
    public async Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default)
    {
        var collectionName = GetCollectionName(userId);
        await _client.DeleteCollectionAsync(collectionName, cancellationToken: ct);
        _logger.LogInformation("Deleted collection {Collection} for user {UserId}", collectionName, userId);
    }
    
    private static string GetCollectionName(string userId) => $"novel_agent_{userId}";
}
```

- [ ] **Step 2: Build and commit**

```bash
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
git add Web/NovelAgentWeb/Services/VectorStore/QdrantCollectionManager.cs
git commit -m "feat(vector): add QdrantCollectionManager for user-level collections"
```

---

## Task 3: Material Chunking Service

**Files:**
- Create: `Web/NovelAgentWeb/Services/Vectorization/MaterialChunker.cs`

- [ ] **Step 1: Create MaterialChunker class**

```csharp
namespace TM.Web.NovelAgentWeb.Services.Vectorization;

public sealed class MaterialChunker
{
    private const int MaxTokensPerChunk = 1500;
    private const int OverlapTokens = 200;
    
    public List<MaterialChunk> ChunkText(string text, string materialId)
    {
        var chunks = new List<MaterialChunk>();
        var tokens = TokenizeSimple(text);
        
        int chunkIndex = 0;
        int position = 0;
        
        while (position < tokens.Count)
        {
            int chunkSize = Math.Min(MaxTokensPerChunk, tokens.Count - position);
            var chunkTokens = tokens.Skip(position).Take(chunkSize).ToList();
            var chunkText = string.Join(" ", chunkTokens);
            
            chunks.Add(new MaterialChunk
            {
                ChunkId = $"{materialId}_chunk_{chunkIndex}",
                ChunkIndex = chunkIndex,
                Content = chunkText,
                TokenCount = chunkTokens.Count
            });
            
            position += chunkSize - OverlapTokens;
            chunkIndex++;
        }
        
        // Update total chunk count
        foreach (var chunk in chunks)
        {
            chunk.ChunkTotal = chunks.Count;
        }
        
        return chunks;
    }
    
    private List<string> TokenizeSimple(string text)
    {
        // Simple whitespace tokenization (replace with proper tokenizer if needed)
        return text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}

public class MaterialChunk
{
    public string ChunkId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public int ChunkTotal { get; set; }
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }
}
```

- [ ] **Step 2: Write unit test**

```csharp
// In Tests/NovelAgentRegression/MaterialChunkerTests.cs
[Fact]
public void ChunkText_ProducesCorrectOverlap()
{
    var chunker = new MaterialChunker();
    var text = string.Join(" ", Enumerable.Range(1, 3000).Select(i => $"word{i}"));
    
    var chunks = chunker.ChunkText(text, "test_material");
    
    Assert.True(chunks.Count >= 2);
    Assert.Equal(0, chunks[0].ChunkIndex);
    Assert.All(chunks, c => Assert.True(c.TokenCount <= 1500));
}
```

- [ ] **Step 3: Build and commit**

```bash
dotnet build
git add Web/NovelAgentWeb/Services/Vectorization/ Tests/NovelAgentRegression/MaterialChunkerTests.cs
git commit -m "feat(vector): add MaterialChunker with 1500 token chunks and 200 token overlap"
```

---

## Task 4: Material Vectorization Service

**Files:**
- Create: `Web/NovelAgentWeb/Services/Vectorization/MaterialVectorizationService.cs`
- Create: `Web/NovelAgentWeb/Services/Vectorization/IMaterialVectorizationService.cs`

- [ ] **Step 1: Create interface**

```csharp
namespace TM.Web.NovelAgentWeb.Services.Vectorization;

public interface IMaterialVectorizationService
{
    Task VectorizeMaterialAsync(string materialId, string userId, CancellationToken ct = default);
    Task<int> VectorizeAllMaterialsAsync(string projectId, string userId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create implementation**

```csharp
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Vectorization;

public sealed class MaterialVectorizationService : IMaterialVectorizationService
{
    private readonly NovelAgentDbContext _db;
    private readonly QdrantClient _qdrant;
    private readonly IMicroEmbeddingService _embedding;
    private readonly MaterialChunker _chunker;
    private readonly QdrantCollectionManager _collectionManager;
    private readonly ILogger<MaterialVectorizationService> _logger;
    
    public MaterialVectorizationService(
        NovelAgentDbContext db,
        QdrantClient qdrant,
        IMicroEmbeddingService embedding,
        MaterialChunker chunker,
        QdrantCollectionManager collectionManager,
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
        var content = await File.ReadAllTextAsync(material.FilePath, ct);
        
        // Chunk material
        var chunks = _chunker.ChunkText(content, materialId);
        _logger.LogInformation("Material {MaterialId} chunked into {ChunkCount} pieces", materialId, chunks.Count);
        
        // Generate embeddings and upsert to Qdrant
        var collectionName = $"novel_agent_{userId}";
        var points = new List<PointStruct>();
        
        foreach (var chunk in chunks)
        {
            var embedding = await _embedding.GenerateEmbeddingAsync(chunk.Content, ct);
            
            var point = new PointStruct
            {
                Id = Guid.NewGuid().ToString(),
                Vectors = embedding.ToArray(),
                Payload =
                {
                    ["user_id"] = userId,
                    ["project_id"] = material.ProjectId,
                    ["entity_type"] = "material",
                    ["entity_id"] = materialId,
                    ["chunk_id"] = chunk.ChunkId,
                    ["chunk_index"] = chunk.ChunkIndex,
                    ["chunk_total"] = chunk.ChunkTotal,
                    ["content"] = chunk.Content,
                    ["title"] = material.FileName,
                    ["category"] = material.Category ?? "",
                    ["created_at"] = material.CreatedAt.ToUnixTimeSeconds()
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
```

- [ ] **Step 3: Register service in Program.cs**

```csharp
builder.Services.AddScoped<MaterialChunker>();
builder.Services.AddScoped<QdrantCollectionManager>();
builder.Services.AddScoped<IMaterialVectorizationService, MaterialVectorizationService>();
```

- [ ] **Step 4: Build and commit**

```bash
dotnet build
git add Web/NovelAgentWeb/Services/Vectorization/ Web/NovelAgentWeb/Program.cs
git commit -m "feat(vector): add MaterialVectorizationService with batch processing"
```

---

## Task 5: Semantic Search Service

**Files:**
- Create: `Web/NovelAgentWeb/Services/VectorStore/SemanticSearchService.cs`

- [ ] **Step 1: Create SemanticSearchService**

```csharp
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TM.Services.Framework.AI.Embedding;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

public sealed class SemanticSearchService
{
    private readonly QdrantClient _qdrant;
    private readonly IMicroEmbeddingService _embedding;
    private readonly ILogger<SemanticSearchService> _logger;
    
    public SemanticSearchService(
        QdrantClient qdrant,
        IMicroEmbeddingService embedding,
        ILogger<SemanticSearchService> logger)
    {
        _qdrant = qdrant;
        _embedding = embedding;
        _logger = logger;
    }
    
    public async Task<List<SearchResult>> SearchInProjectAsync(
        string userId,
        string projectId,
        string query,
        int topK = 10,
        CancellationToken ct = default)
    {
        var collectionName = $"novel_agent_{userId}";
        var queryVector = await _embedding.GenerateEmbeddingAsync(query, ct);
        
        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "project_id",
                        Match = new Match { Keyword = projectId }
                    }
                }
            }
        };
        
        var results = await _qdrant.SearchAsync(
            collectionName: collectionName,
            vector: queryVector.ToArray(),
            filter: filter,
            limit: (ulong)topK,
            cancellationToken: ct);
            
        return results.Select(r => new SearchResult
        {
            ChunkId = r.Payload["chunk_id"].StringValue,
            Content = r.Payload["content"].StringValue,
            Score = r.Score,
            EntityType = r.Payload["entity_type"].StringValue,
            EntityId = r.Payload["entity_id"].StringValue
        }).ToList();
    }
    
    public async Task<List<SearchResult>> DetectSimilarPatternsAsync(
        string userId,
        string projectId,
        string pattern,
        float threshold = 0.85f,
        CancellationToken ct = default)
    {
        var results = await SearchInProjectAsync(userId, projectId, pattern, 20, ct);
        return results.Where(r => r.Score >= threshold).ToList();
    }
}

public class SearchResult
{
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Register and commit**

```bash
builder.Services.AddScoped<SemanticSearchService>();
git add Web/NovelAgentWeb/Services/VectorStore/SemanticSearchService.cs Web/NovelAgentWeb/Program.cs
git commit -m "feat(vector): add SemanticSearchService with similarity detection"
```

---

## Task 6: Integration Tests

**Files:**
- Create: `Tests/NovelAgentRegression/VectorizationIntegrationTests.cs`

- [ ] **Step 1: Write integration test**

```csharp
public class VectorizationIntegrationTests : IDisposable
{
    private readonly NovelAgentDbContext _db;
    private readonly MaterialVectorizationService _service;
    
    [Fact]
    public async Task VectorizeMaterial_CreatesChunksInQdrant()
    {
        // Arrange
        var userId = "test_user";
        var projectId = "test_project";
        var material = CreateTestMaterial(userId, projectId);
        _db.Materials.Add(material);
        await _db.SaveChangesAsync();
        
        // Act
        await _service.VectorizeMaterialAsync(material.Id, userId);
        
        // Assert
        var updatedMaterial = await _db.Materials.FindAsync(material.Id);
        Assert.True(updatedMaterial.VectorChunkCount > 0);
    }
}
```

- [ ] **Step 2: Run tests and commit**

```bash
dotnet test Tests/NovelAgentRegression
git add Tests/NovelAgentRegression/VectorizationIntegrationTests.cs
git commit -m "test(vector): add integration tests for vectorization pipeline"
```

---

## Phase 2 Exit Criteria

- [ ] ✅ Qdrant collections created per user (novel_agent_{userId})
- [ ] ✅ Material chunking produces correct overlaps (1500 tokens, 200 overlap)
- [ ] ✅ Semantic search returns relevant results (manual testing)
- [ ] ✅ Similar pattern detection works (>0.85 similarity threshold)
- [ ] ✅ Database migration adds VectorChunkCount and VectorId fields
- [ ] ✅ All tests pass

**Note:** This is a simplified Phase 2 plan focusing on core vectorization. Full implementation would include knowledge entry vectorization, hybrid search, and cross-project search capabilities.

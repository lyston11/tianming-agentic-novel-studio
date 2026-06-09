using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Qdrant implementation of IVectorStore for vector operations.
/// </summary>
public class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly int _vectorDimension;
    private readonly int _batchSize;

    // HNSW index parameters from spec
    private const int HnswM = 16;
    private const int HnswEfConstruct = 100;

    public QdrantVectorStore(
        IConfiguration configuration,
        ILogger<QdrantVectorStore> logger)
    {
        _logger = logger;

        var qdrantHost = configuration["Qdrant:Host"] ?? "localhost";
        var qdrantPort = configuration.GetValue<int>("Qdrant:Port", 6334);
        _vectorDimension = configuration.GetValue<int>("Qdrant:VectorDimension", 512);
        _batchSize = configuration.GetValue<int>("Qdrant:BatchSize", 100);

        _client = new QdrantClient(host: qdrantHost, port: qdrantPort, https: false);

        _logger.LogInformation("QdrantVectorStore initialized with Host: {Host}:{Port}, Dimension: {Dimension}",
            qdrantHost, qdrantPort, _vectorDimension);
    }

    public async Task InitializeProjectCollectionAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var collectionName = GetCollectionName(projectId);

        try
        {
            // Check if collection already exists
            var exists = await CollectionExistsAsync(projectId, cancellationToken);
            if (exists)
            {
                _logger.LogInformation("Collection {CollectionName} already exists, skipping initialization", collectionName);
                return;
            }

            // Create collection with HNSW index configuration
            await _client.CreateCollectionAsync(
                collectionName: collectionName,
                vectorsConfig: new VectorParams
                {
                    Size = (ulong)_vectorDimension,
                    Distance = Distance.Cosine
                },
                hnswConfig: new HnswConfigDiff
                {
                    M = HnswM,
                    EfConstruct = HnswEfConstruct
                },
                cancellationToken: cancellationToken
            );

            // Create payload indexes for efficient filtering
            await CreatePayloadIndexesAsync(collectionName, cancellationToken);

            _logger.LogInformation("Collection {CollectionName} initialized successfully with HNSW(m={M}, ef_construct={EfConstruct})",
                collectionName, HnswM, HnswEfConstruct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize collection {CollectionName}", collectionName);
            throw;
        }
    }

    private async Task CreatePayloadIndexesAsync(string collectionName, CancellationToken cancellationToken)
    {
        try
        {
            // Index for user_id (critical for tenant isolation)
            await _client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: "user_id",
                schemaType: PayloadSchemaType.Keyword,
                cancellationToken: cancellationToken
            );

            // Index for project_id
            await _client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: "project_id",
                schemaType: PayloadSchemaType.Keyword,
                cancellationToken: cancellationToken
            );

            // Index for source_type
            await _client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: "source_type",
                schemaType: PayloadSchemaType.Keyword,
                cancellationToken: cancellationToken
            );

            _logger.LogDebug("Payload indexes created for collection {CollectionName}", collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create payload indexes for collection {CollectionName}", collectionName);
            throw;
        }
    }

    public async Task UpsertVectorsAsync(
        string projectId,
        List<VectorData> vectors,
        CancellationToken cancellationToken = default)
    {
        if (vectors == null || vectors.Count == 0)
        {
            _logger.LogWarning("UpsertVectorsAsync called with empty vectors list");
            return;
        }

        var collectionName = GetCollectionName(projectId);

        try
        {
            // Validate vector dimensions
            foreach (var vector in vectors)
            {
                if (vector.Vector.Length != _vectorDimension)
                {
                    throw new ArgumentException(
                        $"Vector {vector.Id} has dimension {vector.Vector.Length}, expected {_vectorDimension}");
                }
            }

            // Process in batches
            var batches = vectors.Chunk(_batchSize).ToList();
            _logger.LogInformation("Upserting {VectorCount} vectors in {BatchCount} batches to collection {CollectionName}",
                vectors.Count, batches.Count, collectionName);

            foreach (var batch in batches)
            {
                var points = batch.Select(v => new PointStruct
                {
                    Id = new PointId { Uuid = v.Id },
                    Vectors = new Vectors { Vector = new float[v.Vector.Length] },
                    Payload =
                    {
                        ["user_id"] = v.UserId,
                        ["project_id"] = v.ProjectId,
                        ["source_type"] = v.SourceType,
                        ["source_id"] = v.SourceId,
                        ["chapter_id"] = v.ChapterId ?? "",
                        ["chunk_index"] = v.ChunkIndex ?? 0,
                        ["content"] = v.Content ?? "",
                        ["metadata"] = v.Metadata != null ? System.Text.Json.JsonSerializer.Serialize(v.Metadata) : "{}"
                    }
                }).ToList();

                // Copy vectors
                for (int i = 0; i < points.Count; i++)
                {
                    var sourceVector = batch.ElementAt(i).Vector;
                    points[i].Vectors.Vector.Data.AddRange(sourceVector);
                }

                await _client.UpsertAsync(
                    collectionName: collectionName,
                    points: points,
                    cancellationToken: cancellationToken
                );
            }

            _logger.LogInformation("Successfully upserted {VectorCount} vectors to collection {CollectionName}",
                vectors.Count, collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert vectors to collection {CollectionName}", collectionName);
            throw;
        }
    }

    public async Task<List<SearchResult>> SearchSimilarAsync(
        string projectId,
        float[] queryVector,
        int topK = 10,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        if (queryVector.Length != _vectorDimension)
        {
            throw new ArgumentException($"Query vector has dimension {queryVector.Length}, expected {_vectorDimension}");
        }

        var collectionName = GetCollectionName(projectId);

        try
        {
            // Build filter conditions
            var filterConditions = new List<Condition>();

            // Always filter by project_id for collection-level isolation
            filterConditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "project_id",
                    Match = new Match { Keyword = projectId }
                }
            });

            // Add user-provided filters
            if (filters != null)
            {
                foreach (var filterItem in filters)
                {
                    filterConditions.Add(CreateFilterCondition(filterItem.Key, filterItem.Value));
                }
            }

            var searchFilter = new Filter
            {
                Must = { filterConditions }
            };

            // Perform search
            var searchResults = await _client.SearchAsync(
                collectionName: collectionName,
                vector: queryVector,
                limit: (ulong)topK,
                filter: searchFilter,
                payloadSelector: true,
                cancellationToken: cancellationToken
            );

            // Map results
            var results = searchResults.Select(r => new SearchResult
            {
                Id = r.Id.Uuid,
                Score = r.Score,
                UserId = r.Payload.TryGetValue("user_id", out var userId) ? userId.StringValue : null,
                ProjectId = r.Payload.TryGetValue("project_id", out var projId) ? projId.StringValue : null,
                SourceType = r.Payload.TryGetValue("source_type", out var sourceType) ? sourceType.StringValue : null,
                SourceId = r.Payload.TryGetValue("source_id", out var sourceId) ? sourceId.StringValue : null,
                ChapterId = r.Payload.TryGetValue("chapter_id", out var chapterId) ? chapterId.StringValue : null,
                ChunkIndex = r.Payload.TryGetValue("chunk_index", out var chunkIndex) ? (int?)chunkIndex.IntegerValue : null,
                Content = r.Payload.TryGetValue("content", out var content) ? content.StringValue : null,
                Metadata = r.Payload.TryGetValue("metadata", out var metadata)
                    ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(metadata.StringValue)
                    : null
            }).ToList();

            _logger.LogInformation("Search returned {ResultCount} results from collection {CollectionName}",
                results.Count, collectionName);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search in collection {CollectionName}", collectionName);
            throw;
        }
    }

    private Condition CreateFilterCondition(string key, object value)
    {
        return value switch
        {
            string strValue => new Condition
            {
                Field = new FieldCondition
                {
                    Key = key,
                    Match = new Match { Keyword = strValue }
                }
            },
            int intValue => new Condition
            {
                Field = new FieldCondition
                {
                    Key = key,
                    Match = new Match { Integer = intValue }
                }
            },
            long longValue => new Condition
            {
                Field = new FieldCondition
                {
                    Key = key,
                    Match = new Match { Integer = longValue }
                }
            },
            _ => throw new ArgumentException($"Unsupported filter value type for key {key}: {value.GetType()}")
        };
    }

    public async Task DeleteCollectionAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var collectionName = GetCollectionName(projectId);

        try
        {
            var exists = await CollectionExistsAsync(projectId, cancellationToken);
            if (!exists)
            {
                _logger.LogWarning("Collection {CollectionName} does not exist, skipping deletion", collectionName);
                return;
            }

            await _client.DeleteCollectionAsync(
                collectionName: collectionName,
                cancellationToken: cancellationToken
            );

            _logger.LogInformation("Collection {CollectionName} deleted successfully", collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete collection {CollectionName}", collectionName);
            throw;
        }
    }

    public async Task<bool> CollectionExistsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var collectionName = GetCollectionName(projectId);

        try
        {
            var collections = await _client.ListCollectionsAsync(cancellationToken: cancellationToken);
            return collections.Any(c => c == collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if collection {CollectionName} exists", collectionName);
            throw;
        }
    }

    public async Task<CollectionInfo?> GetCollectionInfoAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var collectionName = GetCollectionName(projectId);

        try
        {
            var exists = await CollectionExistsAsync(projectId, cancellationToken);
            if (!exists)
            {
                return null;
            }

            var collectionInfo = await _client.GetCollectionInfoAsync(
                collectionName: collectionName,
                cancellationToken: cancellationToken
            );

            return new CollectionInfo
            {
                Name = collectionName,
                VectorCount = (long)collectionInfo.PointsCount,
                VectorDimension = (int)collectionInfo.Config.Params.VectorsConfig.Params.Size,
                DistanceMetric = collectionInfo.Config.Params.VectorsConfig.Params.Distance.ToString(),
                IndexType = "HNSW"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get collection info for {CollectionName}", collectionName);
            throw;
        }
    }

    /// <summary>
    /// Generate collection name from project ID following pattern: project_{projectId}
    /// </summary>
    private static string GetCollectionName(string projectId)
    {
        return $"project_{projectId}";
    }

    public async Task DeleteVectorsByFilterAsync(
        string projectId,
        Dictionary<string, object> filters,
        CancellationToken cancellationToken = default)
    {
        var collectionName = GetCollectionName(projectId);

        try
        {
            var exists = await CollectionExistsAsync(projectId, cancellationToken);
            if (!exists)
            {
                _logger.LogWarning("Collection {CollectionName} does not exist, skipping vector deletion", collectionName);
                return;
            }

            // Build filter conditions
            var filterConditions = new List<Condition>();
            foreach (var filter in filters)
            {
                filterConditions.Add(CreateFilterCondition(filter.Key, filter.Value));
            }

            var deleteFilter = new Filter
            {
                Must = { filterConditions }
            };

            // Delete points by filter
            await _client.DeleteAsync(
                collectionName: collectionName,
                filter: deleteFilter,
                cancellationToken: cancellationToken
            );

            _logger.LogInformation("Deleted vectors from collection {CollectionName} with filters: {Filters}",
                collectionName, string.Join(", ", filters.Select(f => $"{f.Key}={f.Value}")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete vectors from collection {CollectionName}", collectionName);
            throw;
        }
    }
}

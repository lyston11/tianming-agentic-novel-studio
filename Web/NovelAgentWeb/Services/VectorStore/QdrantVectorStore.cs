using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Qdrant implementation of IVectorStore. Collections named novel_agent_{userId}.
/// </summary>
public class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly int _vectorDimension;
    private readonly int _batchSize;

    public QdrantVectorStore(
        QdrantClient client,
        IConfiguration configuration,
        ILogger<QdrantVectorStore> logger)
    {
        _client = client;
        _logger = logger;
        _vectorDimension = configuration.GetValue<int>("Qdrant:VectorDimension", 512);
        _batchSize = configuration.GetValue<int>("Qdrant:BatchSize", 100);
    }

    public static string GetCollectionName(string userId) => $"novel_agent_{userId}";

    public async Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default)
    {
        var collectionName = GetCollectionName(userId);
        if (await UserCollectionExistsAsync(userId, ct))
        {
            _logger.LogDebug("Collection {Collection} already exists", collectionName);
            return;
        }

        await _client.CreateCollectionAsync(collectionName, new VectorParams
        {
            Size = (ulong)_vectorDimension,
            Distance = Distance.Cosine
        }, cancellationToken: ct);

        await CreatePayloadIndexesAsync(collectionName, ct);
        _logger.LogInformation("Created collection {Collection}", collectionName);
    }

    private async Task<bool> UserCollectionExistsAsync(string userId, CancellationToken ct)
    {
        var collections = await _client.ListCollectionsAsync(cancellationToken: ct);
        return collections.Any(c => c == GetCollectionName(userId));
    }

    private async Task CreatePayloadIndexesAsync(string collectionName, CancellationToken ct)
    {
        await _client.CreatePayloadIndexAsync(collectionName, "user_id", PayloadSchemaType.Keyword, cancellationToken: ct);
        await _client.CreatePayloadIndexAsync(collectionName, "project_id", PayloadSchemaType.Keyword, cancellationToken: ct);
        await _client.CreatePayloadIndexAsync(collectionName, "source_type", PayloadSchemaType.Keyword, cancellationToken: ct);
    }

    public async Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default)
    {
        if (vectors == null || vectors.Count == 0) return;

        var collectionName = GetCollectionName(userId);

        foreach (var batch in vectors.Chunk(_batchSize))
        {
            var points = batch.Select(v =>
            {
                if (v.Vector.Length != _vectorDimension)
                    throw new ArgumentException($"Vector {v.Id} dim {v.Vector.Length} != {_vectorDimension}");

                return new PointStruct
                {
                    Id = new PointId { Uuid = v.Id },
                    Vectors = CreateUnnamedVector(v.Vector),
                    Payload =
                    {
                        ["user_id"] = v.UserId,
                        ["project_id"] = v.ProjectId,
                        ["source_type"] = v.SourceType,
                        ["source_id"] = v.SourceId,
                        ["chapter_id"] = v.ChapterId ?? "",
                        ["chunk_index"] = v.ChunkIndex ?? 0,
                        ["content"] = v.Content ?? "",
                    }
                };
            }).ToList();

            await _client.UpsertAsync(collectionName, points, cancellationToken: ct);
        }

        _logger.LogInformation("Upserted {Count} vectors to {Collection}", vectors.Count, collectionName);
    }

#pragma warning disable CS0612 // Current Qdrant server image expects Vector.Data for unnamed dense vectors.
    private static Vectors CreateUnnamedVector(float[] values)
    {
        var vector = new Vector();
        vector.Data.AddRange(values);
        return new Vectors { Vector = vector };
    }
#pragma warning restore CS0612

    public async Task<List<SearchResult>> SearchSimilarAsync(
        string userId, float[] queryVector, int topK = 10,
        Dictionary<string, object>? filters = null, CancellationToken ct = default)
    {
        if (queryVector.Length != _vectorDimension)
            throw new ArgumentException($"Query dim {queryVector.Length} != {_vectorDimension}");

        var collectionName = GetCollectionName(userId);
        var conditions = new List<Condition>
        {
            new() { Field = new FieldCondition { Key = "user_id", Match = new Match { Keyword = userId } } }
        };

        if (filters != null)
        {
            foreach (var f in filters)
                conditions.Add(CreateFilterCondition(f.Key, f.Value));
        }

        var results = await _client.SearchAsync(
            collectionName: collectionName,
            vector: queryVector,
            filter: new Filter { Must = { conditions } },
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: ct);

        return results.Select(r => new SearchResult
        {
            Id = r.Id.Uuid,
            Score = r.Score,
            UserId = GetPayloadString(r.Payload, "user_id"),
            ProjectId = GetPayloadString(r.Payload, "project_id"),
            SourceType = GetPayloadString(r.Payload, "source_type"),
            SourceId = GetPayloadString(r.Payload, "source_id"),
            ChapterId = GetPayloadString(r.Payload, "chapter_id"),
            ChunkIndex = GetPayloadInt(r.Payload, "chunk_index"),
            Content = GetPayloadString(r.Payload, "content"),
        }).ToList();
    }

    public async Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default)
    {
        var collectionName = GetCollectionName(userId);
        if (await UserCollectionExistsAsync(userId, ct))
            await _client.DeleteCollectionAsync(collectionName, cancellationToken: ct);
    }

    public async Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default)
        => await UserCollectionExistsAsync(userId, ct);

    public async Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default)
    {
        if (!await UserCollectionExistsAsync(userId, ct)) return null;
        var info = await _client.GetCollectionInfoAsync(GetCollectionName(userId), cancellationToken: ct);
        return new CollectionInfo
        {
            Name = GetCollectionName(userId),
            VectorCount = (long)info.PointsCount,
            VectorDimension = (int)info.Config.Params.VectorsConfig.Params.Size,
            DistanceMetric = info.Config.Params.VectorsConfig.Params.Distance.ToString(),
        };
    }

    public async Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default)
    {
        var collectionName = GetCollectionName(userId);
        if (!await UserCollectionExistsAsync(userId, ct)) return;

        var conditions = filters.Select(f => CreateFilterCondition(f.Key, f.Value)).ToList();
        await _client.DeleteAsync(collectionName, new Filter { Must = { conditions } }, cancellationToken: ct);
    }

    private static Condition CreateFilterCondition(string key, object value) => value switch
    {
        string s => new Condition { Field = new FieldCondition { Key = key, Match = new Match { Keyword = s } } },
        int i => new Condition { Field = new FieldCondition { Key = key, Match = new Match { Integer = i } } },
        long l => new Condition { Field = new FieldCondition { Key = key, Match = new Match { Integer = l } } },
        _ => throw new ArgumentException($"Unsupported filter type for {key}: {value.GetType()}")
    };

    private static string? GetPayloadString(Google.Protobuf.Collections.MapField<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? v.StringValue : null;

    private static int? GetPayloadInt(Google.Protobuf.Collections.MapField<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? (int)v.IntegerValue : null;
}

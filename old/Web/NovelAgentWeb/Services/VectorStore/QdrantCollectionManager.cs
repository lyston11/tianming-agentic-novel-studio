using Qdrant.Client;
using Qdrant.Client.Grpc;
using Microsoft.Extensions.Configuration;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Manages Qdrant collections at the user level for multi-tenant SaaS architecture.
/// Collections follow the pattern: novel_agent_{userId}
/// </summary>
public class QdrantCollectionManager : IQdrantCollectionManager
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantCollectionManager> _logger;
    private readonly int _vectorDimension;

    private const Distance DistanceMetric = Distance.Cosine;

    public QdrantCollectionManager(
        QdrantClient client,
        IConfiguration configuration,
        ILogger<QdrantCollectionManager> logger)
    {
        _client = client;
        _logger = logger;
        _vectorDimension = configuration.GetValue<int>("Qdrant:VectorDimension", 512);
    }

    /// <summary>
    /// Ensures a collection exists for the given user. Creates it if it doesn't exist.
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if collection was created, false if it already existed</returns>
    public async Task<bool> EnsureUserCollectionAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

        var collectionName = GetCollectionName(userId);

        if (await CollectionExistsAsync(collectionName, ct))
        {
            _logger.LogDebug("Collection {CollectionName} already exists for user {UserId}", collectionName, userId);
            return false;
        }

        _logger.LogInformation("Creating collection {CollectionName} for user {UserId}", collectionName, userId);

        await _client.CreateCollectionAsync(
            collectionName: collectionName,
            vectorsConfig: new VectorParams
            {
                Size = (ulong)_vectorDimension,
                Distance = DistanceMetric
            },
            cancellationToken: ct
        );

        // Create payload indexes for efficient filtering
        await CreatePayloadIndexesAsync(collectionName, ct);

        _logger.LogInformation("Successfully created collection {CollectionName} for user {UserId}", collectionName, userId);
        return true;
    }

    /// <summary>
    /// Checks if a collection exists.
    /// </summary>
    public async Task<bool> CollectionExistsAsync(string collectionName, CancellationToken ct = default)
    {
        try
        {
            var collections = await _client.ListCollectionsAsync(cancellationToken: ct);
            return collections.Any(c => c == collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if collection {CollectionName} exists", collectionName);
            throw;
        }
    }

    /// <summary>
    /// Deletes a user's collection.
    /// </summary>
    public async Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

        var collectionName = GetCollectionName(userId);

        if (!await CollectionExistsAsync(collectionName, ct))
        {
            _logger.LogWarning("Collection {CollectionName} does not exist for user {UserId}", collectionName, userId);
            return;
        }

        _logger.LogInformation("Deleting collection {CollectionName} for user {UserId}", collectionName, userId);

        await _client.DeleteCollectionAsync(
            collectionName: collectionName,
            cancellationToken: ct
        );

        _logger.LogInformation("Successfully deleted collection {CollectionName} for user {UserId}", collectionName, userId);
    }

    /// <summary>
    /// Gets the collection name for a user.
    /// </summary>
    public static string GetCollectionName(string userId)
    {
        return $"novel_agent_{userId}";
    }

    /// <summary>
    /// Creates payload indexes for efficient filtering.
    /// </summary>
    private async Task CreatePayloadIndexesAsync(string collectionName, CancellationToken ct)
    {
        try
        {
            await _client.CreatePayloadIndexAsync(collectionName, "project_id", PayloadSchemaType.Keyword, cancellationToken: ct);
            await _client.CreatePayloadIndexAsync(collectionName, "user_id", PayloadSchemaType.Keyword, cancellationToken: ct);
            await _client.CreatePayloadIndexAsync(collectionName, "source_type", PayloadSchemaType.Keyword, cancellationToken: ct);

            _logger.LogDebug("Payload indexes created for collection {CollectionName}", collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create payload indexes for collection {CollectionName}", collectionName);
            throw;
        }
    }
}

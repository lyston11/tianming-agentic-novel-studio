using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Manages Qdrant collections at the user level for multi-tenant SaaS architecture.
/// Collections follow the pattern: novel_agent_{userId}
/// </summary>
public class QdrantCollectionManager : IQdrantCollectionManager
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantCollectionManager> _logger;

    // Vector configuration
    private const int VectorDimension = 1536; // OpenAI text-embedding-ada-002
    private const Distance DistanceMetric = Distance.Cosine;

    public QdrantCollectionManager(
        QdrantClient client,
        ILogger<QdrantCollectionManager> logger)
    {
        _client = client;
        _logger = logger;
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
                Size = VectorDimension,
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
    private static string GetCollectionName(string userId)
    {
        return $"novel_agent_{userId}";
    }

    /// <summary>
    /// Creates payload indexes for efficient filtering on project_id, entity_type, and category.
    /// </summary>
    private async Task CreatePayloadIndexesAsync(string collectionName, CancellationToken ct)
    {
        try
        {
            // Index for project_id (critical for multi-project filtering)
            await _client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: "project_id",
                schemaType: PayloadSchemaType.Keyword,
                cancellationToken: ct
            );

            // Index for entity_type (e.g., character, location, event)
            await _client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: "entity_type",
                schemaType: PayloadSchemaType.Keyword,
                cancellationToken: ct
            );

            // Index for category (semantic categorization)
            await _client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: "category",
                schemaType: PayloadSchemaType.Keyword,
                cancellationToken: ct
            );

            _logger.LogDebug("Payload indexes created for collection {CollectionName}", collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create payload indexes for collection {CollectionName}", collectionName);
            throw;
        }
    }
}

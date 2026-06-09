namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Manages Qdrant collections at the user level for multi-tenant SaaS architecture.
/// Collections follow the pattern: novel_agent_{userId}
/// </summary>
public interface IQdrantCollectionManager
{
    /// <summary>
    /// Ensures a collection exists for the given user. Creates it if it doesn't exist.
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if collection was created, false if it already existed</returns>
    Task<bool> EnsureUserCollectionAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Checks if a collection exists.
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the collection exists, false otherwise</returns>
    Task<bool> CollectionExistsAsync(string collectionName, CancellationToken ct = default);

    /// <summary>
    /// Deletes a user's collection.
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default);
}

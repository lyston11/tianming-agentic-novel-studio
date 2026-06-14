using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

/// <summary>
/// Service interface for managing knowledge base entries.
/// </summary>
public interface IKnowledgeService
{
    /// <summary>
    /// Creates a new knowledge entry for a project.
    /// </summary>
    Task<KnowledgeResponse> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Creates a knowledge entry from the server-side upload/extraction pipeline.
    /// </summary>
    Task<KnowledgeResponse> CreateExtractedKnowledgeAsync(CreateExtractedKnowledgeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lists all knowledge entries for a specific project.
    /// </summary>
    Task<List<KnowledgeResponse>> ListKnowledgeAsync(string projectId, CancellationToken ct = default);

    /// <summary>
    /// Gets a knowledge entry by ID.
    /// </summary>
    Task<KnowledgeResponse> GetKnowledgeAsync(string knowledgeId, CancellationToken ct = default);

    /// <summary>
    /// Updates a knowledge entry.
    /// </summary>
    Task<KnowledgeResponse> UpdateKnowledgeAsync(string knowledgeId, UpdateKnowledgeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a knowledge entry.
    /// </summary>
    Task DeleteKnowledgeAsync(string knowledgeId, CancellationToken ct = default);

    /// <summary>
    /// Performs semantic search on knowledge base entries.
    /// </summary>
    Task<List<KnowledgeSearchResult>> SearchKnowledgeAsync(SearchKnowledgeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Records usage of a user-level knowledge entry in the current project context.
    /// </summary>
    Task IncrementUsageAsync(
        string knowledgeId,
        string projectId,
        string? sessionId = null,
        string? runId = null,
        CancellationToken ct = default);
}

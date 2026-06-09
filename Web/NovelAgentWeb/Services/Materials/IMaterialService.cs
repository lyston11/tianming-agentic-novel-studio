using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Materials;

/// <summary>
/// Service interface for managing materials (reference documents and source content).
/// </summary>
public interface IMaterialService
{
    /// <summary>
    /// Uploads a material file for a project.
    /// </summary>
    Task<MaterialResponse> UploadMaterialAsync(UploadMaterialRequest request, CancellationToken ct = default);

    /// <summary>
    /// Creates a material with text content (no file upload).
    /// </summary>
    Task<MaterialResponse> CreateMaterialAsync(CreateMaterialRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lists all materials for a specific project.
    /// </summary>
    Task<List<MaterialResponse>> ListMaterialsAsync(string projectId, CancellationToken ct = default);

    /// <summary>
    /// Gets material metadata by ID.
    /// </summary>
    Task<MaterialResponse> GetMaterialAsync(string materialId, CancellationToken ct = default);

    /// <summary>
    /// Gets the full content of a material.
    /// </summary>
    Task<MaterialContentResponse> GetMaterialContentAsync(string materialId, CancellationToken ct = default);

    /// <summary>
    /// Updates material metadata (category and tags only).
    /// </summary>
    Task<MaterialResponse> UpdateMaterialAsync(string materialId, UpdateMaterialRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a material and its associated file.
    /// </summary>
    Task DeleteMaterialAsync(string materialId, CancellationToken ct = default);
}

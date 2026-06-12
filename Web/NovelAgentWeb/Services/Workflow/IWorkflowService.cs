using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Workflow;

/// <summary>
/// Service interface for managing volume arc workflow planning.
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// Gets the project workflow detail document for the current user.
    /// </summary>
    Task<ProjectWorkflowDocument> GetProjectWorkflowAsync(string projectId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new volume arc for a project.
    /// </summary>
    Task<VolumeArcResponse> CreateVolumeArcAsync(CreateVolumeArcRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lists all volume arcs for a specific project.
    /// </summary>
    Task<List<VolumeArcResponse>> ListVolumeArcsAsync(string projectId, CancellationToken ct = default);

    /// <summary>
    /// Gets a volume arc by ID.
    /// </summary>
    Task<VolumeArcResponse> GetVolumeArcAsync(string volumeArcId, CancellationToken ct = default);

    /// <summary>
    /// Updates a volume arc.
    /// </summary>
    Task<VolumeArcResponse> UpdateVolumeArcAsync(string volumeArcId, UpdateVolumeArcRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a volume arc.
    /// </summary>
    Task DeleteVolumeArcAsync(string volumeArcId, CancellationToken ct = default);
}

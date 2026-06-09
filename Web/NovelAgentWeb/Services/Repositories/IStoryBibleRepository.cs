using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Repositories;

/// <summary>
/// Repository interface for StoryBible data access operations.
/// Provides read and write operations for all StoryBible entities with user isolation.
/// </summary>
public interface IStoryBibleRepository
{
    /// <summary>
    /// Load the complete StoryBible for a project, including constitution, arcs, characters, foreshadows, world settings, and agent runs.
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>StoryBible aggregate containing all related entities</returns>
    Task<StoryBible> LoadStoryBibleAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save or update the story constitution.
    /// </summary>
    /// <param name="constitution">Constitution entity to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveConstitutionAsync(StoryConstitution constitution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save or update a volume arc.
    /// </summary>
    /// <param name="volumeArc">Volume arc entity to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveVolumeArcAsync(VolumeArc volumeArc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save or update a character state.
    /// </summary>
    /// <param name="character">Character entity to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveCharacterAsync(Character character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plant a new foreshadow entry in the ledger.
    /// </summary>
    /// <param name="foreshadow">Foreshadow entry to plant</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PlantForeshadowAsync(ForeshadowEntry foreshadow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve an existing foreshadow entry by updating its status and resolution details.
    /// </summary>
    /// <param name="foreshadowId">Foreshadow entry ID</param>
    /// <param name="resolvedInChapter">Chapter identifier where foreshadow was resolved</param>
    /// <param name="resolvedContext">Context of the resolution</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ResolveForeshadowAsync(string foreshadowId, string resolvedInChapter, string? resolvedContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save or update a world setting entry.
    /// </summary>
    /// <param name="worldSetting">World setting entry to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveWorldSettingAsync(WorldSettingEntry worldSetting, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save a new agent run record.
    /// </summary>
    /// <param name="agentRun">Agent run entity to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveAgentRunAsync(AgentRun agentRun, CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregate root representing the complete StoryBible for a project.
/// </summary>
public class StoryBible
{
    public StoryConstitution? Constitution { get; set; }
    public List<VolumeArc> VolumeArcs { get; set; } = new();
    public List<Character> Characters { get; set; } = new();
    public List<ForeshadowEntry> ForeshadowEntries { get; set; } = new();
    public List<WorldSettingEntry> WorldSettings { get; set; } = new();
    public List<AgentRun> AgentRuns { get; set; } = new();
}

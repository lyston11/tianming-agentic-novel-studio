namespace TM.Web.NovelAgentWeb.Services.Memory;

/// <summary>
/// Repository for fine-grained agent memory storage.
/// Abstracts SQLite persistence + distributed cache + IMemoryCache three-tier caching.
/// </summary>
public interface IAgentMemoryRepository
{
    // ===== 读取完整记忆对象 =====

    /// <summary>
    /// Get project memory for a user's project.
    /// </summary>
    Task<ProjectMemory> GetProjectMemoryAsync(string userId, string projectId, CancellationToken ct = default);

    /// <summary>
    /// Get author memory for a user (cross-project).
    /// </summary>
    Task<AuthorMemory> GetAuthorMemoryAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Get execution memory for a user's project.
    /// </summary>
    Task<ExecutionMemory> GetExecutionMemoryAsync(string userId, string projectId, CancellationToken ct = default);

    // ===== 原子更新单个字段 =====

    /// <summary>
    /// Update a single memory field atomically.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="projectId">Project ID (null for cross-project memories like AuthorMemory)</param>
    /// <param name="memoryType">Memory type in format "{scope}.{field}", e.g. "project.long_term_goal"</param>
    /// <param name="value">Field value (will be JSON serialized)</param>
    Task UpdateFieldAsync(string userId, string? projectId, string memoryType, object value, CancellationToken ct = default);

    // ===== 批量更新(事务) =====

    /// <summary>
    /// Update multiple memory fields in a single transaction.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="projectId">Project ID (null for cross-project)</param>
    /// <param name="updates">Dictionary of memoryType → value</param>
    Task UpdateMemoryAsync(string userId, string? projectId, Dictionary<string, object> updates, CancellationToken ct = default);
}

/// <summary>
/// Project-level memory (tied to a specific project).
/// </summary>
public class ProjectMemory
{
    public string? LongTermGoal { get; set; }
    public string? ReaderPromise { get; set; }
    public List<string> Constraints { get; set; } = new();
    public List<string> UnresolvedThreads { get; set; } = new();
    public List<string> ReferencedKnowledgeIds { get; set; } = new();
    public List<string> UsedTropePatterns { get; set; } = new();
}

/// <summary>
/// Author-level memory (cross-project, user profile).
/// </summary>
public class AuthorMemory
{
    public List<string> StyleLikes { get; set; } = new();
    public List<string> StyleDislikes { get; set; } = new();
    public string? ConfirmationTolerance { get; set; }
    public List<string> GenreHabits { get; set; } = new();
    public List<string> FavoriteKnowledgeIds { get; set; } = new();
}

/// <summary>
/// Execution memory (tool failure patterns and repair experience).
/// </summary>
public class ExecutionMemory
{
    public List<string> ToolFailurePatterns { get; set; } = new();
    public List<string> RepeatedBlockers { get; set; } = new();
    public List<string> SuccessfulRepairNotes { get; set; } = new();
}

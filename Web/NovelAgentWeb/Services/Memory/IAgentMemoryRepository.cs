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
    /// Get session memory for a user's project session.
    /// </summary>
    Task<SessionMemory> GetSessionMemoryAsync(string userId, string projectId, string sessionId, CancellationToken ct = default);

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

    /// <summary>
    /// Union list memory fields with the latest stored database values.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="projectId">Project ID (null for cross-project)</param>
    /// <param name="updates">Dictionary of memoryType → list values to merge case-insensitively</param>
    Task UnionMemoryAsync(string userId, string? projectId, Dictionary<string, IReadOnlyList<string>> updates, CancellationToken ct = default);

    /// <summary>
    /// Update multiple session memory fields in a single transaction.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="projectId">Project ID</param>
    /// <param name="sessionId">Session ID</param>
    /// <param name="updates">Dictionary of session memoryType → value</param>
    Task UpdateSessionMemoryAsync(string userId, string projectId, string sessionId, Dictionary<string, object> updates, CancellationToken ct = default);
}

/// <summary>
/// Session-level memory (tied to a specific project session).
/// </summary>
public class SessionMemory
{
    public string CurrentGoal { get; set; } = string.Empty;
    public List<string> OpenQuestions { get; set; } = new();
    public List<string> ShortTermPreferences { get; set; } = new();
    public List<string> RecentObservations { get; set; } = new();
    public string? PendingToolName { get; set; }
    public string? LastIntent { get; set; }
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
    public List<string> ImportedKnowledgeIds { get; set; } = new();
    public List<KnowledgeInventoryItem> KnowledgeInventory { get; set; } = new();
    public List<string> UsedTropePatterns { get; set; } = new();
}

public class KnowledgeInventoryItem
{
    public string KnowledgeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int Weight { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ProjectUsageStatus { get; set; } = "imported";
    public int ProjectUsageCount { get; set; }
    public DateTime? ProjectLastUsedAt { get; set; }
}

/// <summary>
/// Author-level memory (cross-project, user profile).
/// </summary>
public class AuthorMemory
{
    public string? DisplayName { get; set; }
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
    public List<string> KnowledgeProcessingFailures { get; set; } = new();
}

using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public class AgentMemoryRepository : IAgentMemoryRepository
{
    private readonly NovelAgentDbContext _context;
    private readonly IDistributedCacheService _redisCache;
    private readonly IMemoryCacheService _memoryCache;
    private readonly IVectorStore _vectorStore;
    private readonly IMicroEmbeddingService _embedding;
    private readonly ILogger<AgentMemoryRepository> _logger;
    private readonly IAgentMemoryVersionService? _versions;

    private static readonly TimeSpan MemoryCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RedisCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MemoryLocks = new(StringComparer.Ordinal);
    private static readonly HashSet<string> UnionListMemoryTypes = new(StringComparer.Ordinal)
    {
        "project.referenced_knowledge_ids",
        "project.used_trope_patterns",
        "execution.successful_repairs",
        "execution.repeated_blockers",
        "execution.tool_failures",
        "execution.knowledge_processing_failures",
        "author.style_dislikes"
    };

    public AgentMemoryRepository(
        NovelAgentDbContext context,
        IDistributedCacheService redisCache,
        IMemoryCacheService memoryCache,
        IVectorStore vectorStore,
        IMicroEmbeddingService embedding,
        ILogger<AgentMemoryRepository> logger,
        IAgentMemoryVersionService? versions = null)
    {
        _context = context;
        _redisCache = redisCache;
        _memoryCache = memoryCache;
        _vectorStore = vectorStore;
        _embedding = embedding;
        _logger = logger;
        _versions = versions;
    }

    public async Task<ProjectMemory> GetProjectMemoryAsync(string userId, string projectId, CancellationToken ct = default)
    {
        var cacheKey = $"memory:project:{userId}:{projectId}";

        return await _memoryCache.GetOrSetAsync(
            cacheKey,
            async () =>
            {
                var cached = await _redisCache.GetAsync<ProjectMemory>(cacheKey, ct);
                if (cached != null)
                {
                    _logger.LogDebug("ProjectMemory cache hit (Redis) for user {UserId}, project {ProjectId}", userId, projectId);
                    return cached;
                }

                var rows = await _context.AgentMemories
                    .AsNoTracking()
                    .Where(m => m.UserId == userId && m.ProjectId == projectId && m.SessionId == null && m.MemoryType.StartsWith("project."))
                    .ToListAsync(ct);

                var memory = new ProjectMemory
                {
                    LongTermGoal = GetField<string>(rows, "project.long_term_goal"),
                    ReaderPromise = GetField<string>(rows, "project.reader_promise"),
                    Constraints = GetField<List<string>>(rows, "project.constraints") ?? new(),
                    UnresolvedThreads = GetField<List<string>>(rows, "project.unresolved_threads") ?? new(),
                    ReferencedKnowledgeIds = GetField<List<string>>(rows, "project.referenced_knowledge_ids") ?? new(),
                    ImportedKnowledgeIds = GetField<List<string>>(rows, "project.imported_knowledge_ids") ?? new(),
                    KnowledgeInventory = GetField<List<KnowledgeInventoryItem>>(rows, "project.knowledge_inventory") ?? new(),
                    UsedTropePatterns = GetField<List<string>>(rows, "project.used_trope_patterns") ?? new()
                };

                await _redisCache.SetAsync(cacheKey, memory, RedisCacheDuration, ct);

                _logger.LogDebug("ProjectMemory loaded from database for user {UserId}, project {ProjectId}", userId, projectId);
                return memory;
            },
            MemoryCacheDuration,
            ct);
    }

    public async Task<SessionMemory> GetSessionMemoryAsync(string userId, string projectId, string sessionId, CancellationToken ct = default)
    {
        var cacheKey = BuildSessionCacheKey(userId, sessionId);

        return await _memoryCache.GetOrSetAsync(
            cacheKey,
            async () =>
            {
                var cached = await _redisCache.GetAsync<SessionMemory>(cacheKey, ct);
                if (cached != null)
                {
                    _logger.LogDebug("SessionMemory cache hit (Redis) for user {UserId}, project {ProjectId}, session {SessionId}", userId, projectId, sessionId);
                    return cached;
                }

                var rows = await _context.AgentMemories
                    .AsNoTracking()
                    .Where(m =>
                        m.UserId == userId &&
                        m.ProjectId == null &&
                        m.SessionId == sessionId &&
                        m.MemoryType.StartsWith("session."))
                    .ToListAsync(ct);

                var memory = new SessionMemory
                {
                    CurrentGoal = GetField<string>(rows, "session.current_goal") ?? string.Empty,
                    OpenQuestions = GetField<List<string>>(rows, "session.open_questions") ?? new(),
                    ShortTermPreferences = GetField<List<string>>(rows, "session.short_term_preferences") ?? new(),
                    RecentObservations = GetField<List<string>>(rows, "session.recent_observations") ?? new(),
                    PendingToolName = GetField<string>(rows, "session.pending_tool_name"),
                    LastIntent = GetField<string>(rows, "session.last_intent")
                };

                await _redisCache.SetAsync(cacheKey, memory, RedisCacheDuration, ct);

                _logger.LogDebug("SessionMemory loaded from database for user {UserId}, session {SessionId}", userId, sessionId);
                return memory;
            },
            MemoryCacheDuration,
            ct);
    }

    public async Task<AuthorMemory> GetAuthorMemoryAsync(string userId, CancellationToken ct = default)
    {
        var cacheKey = $"memory:author:{userId}";

        return await _memoryCache.GetOrSetAsync(
            cacheKey,
            async () =>
            {
                var cached = await _redisCache.GetAsync<AuthorMemory>(cacheKey, ct);
                if (cached != null)
                {
                    _logger.LogDebug("AuthorMemory cache hit (Redis) for user {UserId}", userId);
                    return cached;
                }

                var rows = await _context.AgentMemories
                    .AsNoTracking()
                    .Where(m => m.UserId == userId && m.ProjectId == null && m.SessionId == null && m.MemoryType.StartsWith("author."))
                    .ToListAsync(ct);

                var memory = new AuthorMemory
                {
                    StyleLikes = GetField<List<string>>(rows, "author.style_likes") ?? new(),
                    StyleDislikes = GetField<List<string>>(rows, "author.style_dislikes") ?? new(),
                    ConfirmationTolerance = GetField<string>(rows, "author.confirmation_tolerance"),
                    GenreHabits = GetField<List<string>>(rows, "author.genre_habits") ?? new(),
                    FavoriteKnowledgeIds = GetField<List<string>>(rows, "author.favorite_knowledge_ids") ?? new()
                };

                await _redisCache.SetAsync(cacheKey, memory, RedisCacheDuration, ct);

                _logger.LogDebug("AuthorMemory loaded from database for user {UserId}", userId);
                return memory;
            },
            MemoryCacheDuration,
            ct);
    }

    public async Task<ExecutionMemory> GetExecutionMemoryAsync(string userId, string projectId, CancellationToken ct = default)
    {
        var storeProjectId = AgentMemoryScopes.ToStoreProjectId(projectId);
        var cacheProjectId = AgentMemoryScopes.ToExecutionCacheProjectId(projectId);
        var cacheKey = $"memory:execution:{userId}:{cacheProjectId}";

        return await _memoryCache.GetOrSetAsync(
            cacheKey,
            async () =>
            {
                var cached = await _redisCache.GetAsync<ExecutionMemory>(cacheKey, ct);
                if (cached != null)
                {
                    _logger.LogDebug("ExecutionMemory cache hit (Redis) for user {UserId}, project {ProjectId}", userId, projectId);
                    return cached;
                }

                var rows = await _context.AgentMemories
                    .AsNoTracking()
                    .Where(m => m.UserId == userId && m.ProjectId == storeProjectId && m.SessionId == null && m.MemoryType.StartsWith("execution."))
                    .ToListAsync(ct);

                var memory = new ExecutionMemory
                {
                    ToolFailurePatterns = GetField<List<string>>(rows, "execution.tool_failures") ?? new(),
                    RepeatedBlockers = GetField<List<string>>(rows, "execution.repeated_blockers") ?? new(),
                    SuccessfulRepairNotes = GetField<List<string>>(rows, "execution.successful_repairs") ?? new(),
                    KnowledgeProcessingFailures = GetField<List<string>>(rows, "execution.knowledge_processing_failures") ?? new()
                };

                await _redisCache.SetAsync(cacheKey, memory, RedisCacheDuration, ct);

                _logger.LogDebug("ExecutionMemory loaded from database for user {UserId}, project {ProjectId}", userId, cacheProjectId);
                return memory;
            },
            MemoryCacheDuration,
            ct);
    }

    public async Task UpdateFieldAsync(string userId, string? projectId, string memoryType, object value, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value);

        var existing = await _context.AgentMemories
            .FirstOrDefaultAsync(m =>
                m.UserId == userId &&
                m.ProjectId == projectId &&
                m.SessionId == null &&
                m.MemoryType == memoryType, ct);

        if (existing != null)
        {
            existing.Content = json;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.AgentMemories.Add(new Data.Entities.AgentMemory
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ProjectId = projectId,
                MemoryType = memoryType,
                MemoryKey = GetMemoryKey(memoryType),
                Content = json,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(ct);
        await InvalidateCacheAsync(userId, projectId, memoryType);
        await RecordMemoryWritesAsync(
            userId,
            projectId,
            null,
            new[] { memoryType },
            "field_update",
            new Dictionary<string, object> { [memoryType] = value },
            ct);

        _logger.LogDebug("Updated memory field {MemoryType} for user {UserId}, project {ProjectId}", memoryType, userId, projectId);

        await VectorizeProjectMemoryFieldsAsync(
            userId,
            projectId,
            new Dictionary<string, object> { [memoryType] = value },
            ct);
    }

    public async Task UpdateMemoryAsync(string userId, string? projectId, Dictionary<string, object> updates, CancellationToken ct = default)
    {
        var isInMemory = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";
        IDbContextTransaction? transaction = null;

        if (!isInMemory)
        {
            transaction = await _context.Database.BeginTransactionAsync(ct);
        }

        try
        {
            foreach (var (memoryType, value) in updates)
            {
                var json = JsonSerializer.Serialize(value);

                var existing = await _context.AgentMemories
                    .FirstOrDefaultAsync(m =>
                        m.UserId == userId &&
                        m.ProjectId == projectId &&
                        m.SessionId == null &&
                        m.MemoryType == memoryType, ct);

                if (existing != null)
                {
                    existing.Content = json;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _context.AgentMemories.Add(new Data.Entities.AgentMemory
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserId = userId,
                        ProjectId = projectId,
                        MemoryType = memoryType,
                        MemoryKey = GetMemoryKey(memoryType),
                        Content = json,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync(ct);

            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            foreach (var memoryType in updates.Keys)
            {
                await InvalidateCacheAsync(userId, projectId, memoryType);
            }

            await RecordMemoryWritesAsync(
                userId,
                projectId,
                null,
                updates.Keys,
                "batch_update",
                updates,
                ct);
            await VectorizeProjectMemoryFieldsAsync(userId, projectId, updates, ct);

            _logger.LogDebug("Batch updated {Count} memory fields for user {UserId}, project {ProjectId}", updates.Count, userId, projectId);
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(ct);
            }
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    public async Task UnionMemoryAsync(string userId, string? projectId, Dictionary<string, IReadOnlyList<string>> updates, CancellationToken ct = default)
    {
        var storeProjectId = AgentMemoryScopes.ToStoreProjectId(projectId);
        var lockProjectId = AgentMemoryScopes.IsProjectless(projectId) ? AgentMemoryScopes.ProjectlessProjectId : projectId;
        foreach (var memoryType in updates.Keys)
        {
            if (!UnionListMemoryTypes.Contains(memoryType))
            {
                throw new ArgumentException($"Memory type does not support list union updates: {memoryType}", nameof(updates));
            }

            var scope = memoryType.Split('.')[0];
            if (scope == "author" && projectId != null)
            {
                throw new ArgumentException("Author memory union updates must use a null projectId.", nameof(projectId));
            }

            if (scope == "project" && string.IsNullOrWhiteSpace(storeProjectId))
            {
                throw new ArgumentException("Project memory union updates require a projectId.", nameof(projectId));
            }
        }

        if (updates.Count == 0)
        {
            return;
        }

        var lockKey = $"{userId}:{lockProjectId ?? "<global>"}";
        var memoryLock = MemoryLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await memoryLock.WaitAsync(ct);
        try
        {
            var isInMemory = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";
            IDbContextTransaction? transaction = null;

            if (!isInMemory)
            {
                transaction = await _context.Database.BeginTransactionAsync(ct);
            }

            try
            {
                foreach (var (memoryType, incomingValues) in updates)
                {
                    var existing = await _context.AgentMemories
                        .FirstOrDefaultAsync(m =>
                            m.UserId == userId &&
                            m.ProjectId == storeProjectId &&
                            m.SessionId == null &&
                            m.MemoryType == memoryType, ct);

                    var merged = ReadStringList(existing?.Content);
                    foreach (var value in incomingValues.Select(v => v.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)))
                    {
                        if (!merged.Contains(value, StringComparer.OrdinalIgnoreCase))
                        {
                            merged.Add(value);
                        }
                    }

                    var json = JsonSerializer.Serialize(merged);
                    var now = DateTime.UtcNow;

                    if (existing != null)
                    {
                        existing.Content = json;
                        existing.MemoryKey = GetMemoryKey(memoryType);
                        existing.UpdatedAt = now;
                    }
                    else
                    {
                        _context.AgentMemories.Add(new Data.Entities.AgentMemory
                        {
                            Id = Guid.NewGuid().ToString(),
                            UserId = userId,
                            ProjectId = storeProjectId,
                            MemoryType = memoryType,
                            MemoryKey = GetMemoryKey(memoryType),
                            Content = json,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                }

                await _context.SaveChangesAsync(ct);

                if (transaction != null)
                {
                    await transaction.CommitAsync(ct);
                }
            }
            catch
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(ct);
                }
                throw;
            }
            finally
            {
                transaction?.Dispose();
            }
        }
        finally
        {
            memoryLock.Release();
        }

        foreach (var memoryType in updates.Keys)
        {
            await InvalidateCacheAsync(userId, projectId, memoryType);
        }

        await RecordMemoryWritesAsync(
            userId,
            storeProjectId,
            null,
            updates.Keys,
            "union_update",
            updates.ToDictionary(kv => kv.Key, kv => (object)kv.Value.ToList(), StringComparer.OrdinalIgnoreCase),
            ct);

        _logger.LogDebug("Union updated {Count} memory fields for user {UserId}, project {ProjectId}", updates.Count, userId, projectId);
    }

    public async Task UpdateSessionMemoryAsync(string userId, string projectId, string sessionId, Dictionary<string, object> updates, CancellationToken ct = default)
    {
        if (updates.Keys.Any(memoryType => !memoryType.StartsWith("session.", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Session memory updates must use session.* memory types.", nameof(updates));
        }

        var isInMemory = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";
        IDbContextTransaction? transaction = null;

        if (!isInMemory)
        {
            transaction = await _context.Database.BeginTransactionAsync(ct);
        }

        try
        {
            foreach (var (memoryType, value) in updates)
            {
                var json = JsonSerializer.Serialize(value);

                var existing = await _context.AgentMemories
                    .FirstOrDefaultAsync(m =>
                        m.UserId == userId &&
                        m.ProjectId == null &&
                        m.SessionId == sessionId &&
                        m.MemoryType == memoryType, ct);

                if (existing != null)
                {
                    existing.Content = json;
                    existing.MemoryKey = GetMemoryKey(memoryType);
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _context.AgentMemories.Add(new Data.Entities.AgentMemory
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserId = userId,
                        ProjectId = null,
                        SessionId = sessionId,
                        MemoryType = memoryType,
                        MemoryKey = GetMemoryKey(memoryType),
                        Content = json,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync(ct);

            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            await InvalidateSessionCacheAsync(userId, projectId, sessionId, ct);
            await RecordMemoryWritesAsync(
                userId,
                null,
                sessionId,
                updates.Keys,
                "session_update",
                updates,
                ct);

            _logger.LogDebug(
                "Batch updated {Count} session memory fields for user {UserId}, project {ProjectId}, session {SessionId}",
                updates.Count,
                userId,
                projectId,
                sessionId);
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(ct);
            }
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private static T? GetField<T>(List<Data.Entities.AgentMemory> rows, string memoryType)
    {
        var row = rows.FirstOrDefault(r => r.MemoryType == memoryType);
        if (row == null || string.IsNullOrEmpty(row.Content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(row.Content);
    }

    private static List<string> ReadStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Stored memory content is not a list of strings.", ex);
        }
    }

    private async Task InvalidateCacheAsync(string userId, string? projectId, string memoryType)
    {
        var scope = memoryType.Split('.')[0];

        var cacheKey = scope switch
        {
            "project" => $"memory:project:{userId}:{projectId}",
            "author" => $"memory:author:{userId}",
            "execution" => $"memory:execution:{userId}:{projectId}",
            "session" => null,
            _ => throw new ArgumentException($"Unknown memory type: {memoryType}")
        };

        if (cacheKey == null)
        {
            _logger.LogDebug("Skipped session memory cache invalidation without session scope for user {UserId}, project {ProjectId}", userId, projectId);
            return;
        }

        _memoryCache.Remove(cacheKey);
        await _redisCache.RemoveAsync(cacheKey);

        _logger.LogDebug("Invalidated cache for key {CacheKey}", cacheKey);
    }

    private async Task InvalidateSessionCacheAsync(string userId, string projectId, string sessionId, CancellationToken ct)
    {
        var cacheKey = BuildSessionCacheKey(userId, sessionId);
        _memoryCache.Remove(cacheKey);
        await _redisCache.RemoveAsync(cacheKey, ct);

        _logger.LogDebug("Invalidated cache for key {CacheKey}", cacheKey);
    }

    private async Task RecordMemoryWritesAsync(
        string userId,
        string? projectId,
        string? sessionId,
        IEnumerable<string> memoryTypes,
        string triggerType,
        object payload,
        CancellationToken ct)
    {
        var normalizedTypes = memoryTypes
            .Where(memoryType => !string.IsNullOrWhiteSpace(memoryType))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalizedTypes.Count == 0)
        {
            return;
        }

        var payloadJson = JsonSerializer.Serialize(payload);
        foreach (var memoryType in normalizedTypes)
        {
            var scope = GetMemoryScope(memoryType);
            _context.AgentMemoryEvents.Add(new AgentMemoryEvent
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ProjectId = scope is "project" or "execution" ? projectId : null,
                SessionId = scope == "session" ? sessionId : null,
                SourceType = "memory_repository",
                TriggerType = triggerType,
                MemoryScope = scope,
                MemoryKey = memoryType,
                PayloadJson = payloadJson,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(ct);

        if (_versions == null)
        {
            return;
        }

        foreach (var scope in normalizedTypes.Select(GetMemoryScope).Distinct(StringComparer.Ordinal))
        {
            var scopedProjectId = scope is "project" or "execution" ? projectId : null;
            var scopedSessionId = scope == "session" ? sessionId : null;
            await _versions.BumpAsync(userId, scopedProjectId, scopedSessionId, scope, ct);
        }
    }

    private async Task VectorizeProjectMemoryFieldsAsync(
        string userId,
        string? projectId,
        IReadOnlyDictionary<string, object> updates,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        var vectors = new List<VectorData>();
        foreach (var (memoryType, value) in updates)
        {
            if (!IsVectorizedProjectMemoryType(memoryType) ||
                value is not string text ||
                string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            try
            {
                var vectorTask = _embedding.EncodeAsync(text, EmbeddingMode.Passage, ct);
                if (vectorTask == null)
                {
                    continue;
                }

                var vector = await vectorTask;
                vectors.Add(new VectorData
                {
                    Id = $"memory_{userId}_{projectId}_{memoryType}",
                    Vector = vector,
                    UserId = userId,
                    ProjectId = projectId,
                    SourceType = "memory",
                    SourceId = memoryType,
                    Content = text,
                    Metadata = new Dictionary<string, object> { { "memory_type", memoryType } }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to vectorize memory field {MemoryType} for user {UserId}, project {ProjectId}", memoryType, userId, projectId);
            }
        }

        if (vectors.Count == 0)
        {
            return;
        }

        try
        {
            var upsertTask = _vectorStore.UpsertVectorsAsync(userId, vectors, ct);
            if (upsertTask != null)
            {
                await upsertTask;
            }

            _logger.LogDebug("Vectorized {Count} project memory fields for user {UserId}, project {ProjectId}", vectors.Count, userId, projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert project memory vectors for user {UserId}, project {ProjectId}", userId, projectId);
        }
    }

    private static bool IsVectorizedProjectMemoryType(string memoryType) =>
        memoryType is "project.long_term_goal" or "project.reader_promise";

    private static string BuildSessionCacheKey(string userId, string sessionId) =>
        $"memory:session:{userId}:{sessionId}";

    private static string GetMemoryScope(string memoryType)
    {
        var separator = memoryType.IndexOf('.');
        return separator > 0 ? memoryType[..separator] : memoryType;
    }

    private static string GetMemoryKey(string memoryType)
    {
        var separator = memoryType.IndexOf('.');
        return separator >= 0 && separator < memoryType.Length - 1
            ? memoryType[(separator + 1)..]
            : memoryType;
    }
}

using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public class AgentMemoryRepository : IAgentMemoryRepository
{
    private readonly NovelAgentDbContext _context;
    private readonly IDistributedCacheService _redisCache;
    private readonly IMemoryCacheService _memoryCache;
    private readonly ILogger<AgentMemoryRepository> _logger;
    private readonly IProductionTruthStore _truthStore;
    private readonly IAgentMemoryVersionService? _versions;
    private readonly IDistributedLockService? _distributedLocks;
    private readonly string _memoryLockOwner;

    private static readonly TimeSpan MemoryCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RedisCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MemoryDistributedLockTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MemoryDistributedLockAcquireTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MemoryDistributedLockRetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MemoryLocks = new(StringComparer.Ordinal);
    private static readonly HashSet<string> UnionListMemoryTypes = new(StringComparer.Ordinal)
    {
        "project.referenced_knowledge_ids",
        "project.constraints",
        "project.used_trope_patterns",
        "execution.successful_repairs",
        "execution.repeated_blockers",
        "execution.tool_failures",
        "execution.knowledge_processing_failures",
        "author.style_likes",
        "author.genre_habits",
        "author.favorite_knowledge_ids",
        "author.style_dislikes"
    };

    public AgentMemoryRepository(
        NovelAgentDbContext context,
        IDistributedCacheService redisCache,
        IMemoryCacheService memoryCache,
        ILogger<AgentMemoryRepository> logger,
        IProductionTruthStore truthStore,
        IAgentMemoryVersionService? versions = null,
        IDistributedLockService? distributedLocks = null)
    {
        _context = context;
        _redisCache = redisCache;
        _memoryCache = memoryCache;
        _logger = logger;
        _truthStore = truthStore;
        _versions = versions;
        _distributedLocks = distributedLocks;
        _memoryLockOwner = $"agent-memory:{Environment.MachineName}:{Guid.NewGuid():N}";
    }

    public async Task<ProjectMemory> GetProjectMemoryAsync(string userId, string projectId, CancellationToken ct = default, string? runId = null, string? sessionId = null)
    {
        var cacheKey = $"memory:project:{userId}:{projectId}";

        var cachedProjectMemory = await _memoryCache.GetOrSetAsync(
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
                    // UnresolvedThreads removed
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
        await RecordMemoryReadAsync(
            userId,
            projectId,
            sessionId,
            runId,
            "project",
            ProjectMemoryKeys,
            nameof(GetProjectMemoryAsync),
            ct);
        return cachedProjectMemory ?? new ProjectMemory();
    }

    public async Task<SessionMemory> GetSessionMemoryAsync(string userId, string projectId, string sessionId, CancellationToken ct = default, string? runId = null)
    {
        var cacheKey = BuildSessionCacheKey(userId, sessionId);

        var cachedSessionMemory = await _memoryCache.GetOrSetAsync(
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
        await RecordMemoryReadAsync(
            userId,
            projectId,
            sessionId,
            runId,
            "session",
            SessionMemoryKeys,
            nameof(GetSessionMemoryAsync),
            ct);
        return cachedSessionMemory ?? new SessionMemory();
    }

    public async Task<AuthorMemory> GetAuthorMemoryAsync(string userId, CancellationToken ct = default, string? runId = null, string? sessionId = null)
    {
        var cacheKey = $"memory:author:{userId}";

        var cachedAuthorMemory = await _memoryCache.GetOrSetAsync(
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
                    DisplayName = GetField<string>(rows, "author.display_name"),
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
        await RecordMemoryReadAsync(
            userId,
            null,
            sessionId,
            runId,
            "author",
            AuthorMemoryKeys,
            nameof(GetAuthorMemoryAsync),
            ct);
        return cachedAuthorMemory ?? new AuthorMemory();
    }

    public async Task<ExecutionMemory> GetExecutionMemoryAsync(string userId, string projectId, CancellationToken ct = default, string? runId = null, string? sessionId = null)
    {
        var storeProjectId = AgentMemoryScopes.ToStoreProjectId(projectId);
        var cacheProjectId = AgentMemoryScopes.ToExecutionCacheProjectId(projectId);
        var cacheKey = $"memory:execution:{userId}:{cacheProjectId}";

        var cachedExecutionMemory = await _memoryCache.GetOrSetAsync(
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
        await RecordMemoryReadAsync(
            userId,
            storeProjectId,
            sessionId,
            runId,
            "execution",
            ExecutionMemoryKeys,
            nameof(GetExecutionMemoryAsync),
            ct);
        return cachedExecutionMemory ?? new ExecutionMemory();
    }

    public async Task UpdateFieldAsync(string userId, string? projectId, string memoryType, object value, CancellationToken ct = default)
    {
        await using var writeLock = await AcquireMemoryWriteLockAsync(userId, projectId, ct).ConfigureAwait(false);
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

        await EnqueueProjectMemoryIndexOutboxAsync(
            userId,
            projectId,
            new Dictionary<string, object> { [memoryType] = value },
            ct);
    }

    public async Task UpdateMemoryAsync(string userId, string? projectId, Dictionary<string, object> updates, CancellationToken ct = default)
    {
        if (updates.Count == 0)
            return;

        await using var writeLock = await AcquireMemoryWriteLockAsync(userId, projectId, ct).ConfigureAwait(false);
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
            await EnqueueProjectMemoryIndexOutboxAsync(userId, projectId, updates, ct);

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
        var distributedLockKey = BuildMemoryDistributedLockKey(userId, lockProjectId);
        var lease = await AcquireMemoryDistributedLockAsync(distributedLockKey, ct).ConfigureAwait(false);
        var localLockAcquired = false;
        try
        {
            await memoryLock.WaitAsync(ct);
            localLockAcquired = true;

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
            if (localLockAcquired)
            {
                memoryLock.Release();
            }

            await ReleaseMemoryDistributedLockAsync(lease, CancellationToken.None).ConfigureAwait(false);
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

    private async Task<DistributedLockLease?> AcquireMemoryDistributedLockAsync(string lockKey, CancellationToken ct)
    {
        if (_distributedLocks == null)
            return null;

        var startedAt = DateTime.UtcNow;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var lease = await _distributedLocks
                .TryAcquireAsync(lockKey, MemoryDistributedLockTtl, _memoryLockOwner, ct)
                .ConfigureAwait(false);
            if (lease != null)
                return lease;

            if (DateTime.UtcNow - startedAt >= MemoryDistributedLockAcquireTimeout)
                throw new TimeoutException($"Timed out acquiring agent memory distributed lock '{lockKey}'.");

            await Task.Delay(MemoryDistributedLockRetryDelay, ct).ConfigureAwait(false);
        }
    }

    private async Task ReleaseMemoryDistributedLockAsync(DistributedLockLease? lease, CancellationToken ct)
    {
        if (lease == null || _distributedLocks == null)
            return;

        await _distributedLocks.ReleaseAsync(lease, ct).ConfigureAwait(false);
    }

    private async Task<MemoryWriteLease> AcquireMemoryWriteLockAsync(
        string userId,
        string? scopeId,
        CancellationToken ct)
    {
        var normalizedScope = string.IsNullOrWhiteSpace(scopeId) ? "<global>" : scopeId.Trim();
        var localKey = $"{userId}:{normalizedScope}";
        var localLock = MemoryLocks.GetOrAdd(localKey, _ => new SemaphoreSlim(1, 1));
        var distributedLease = await AcquireMemoryDistributedLockAsync(
                BuildMemoryDistributedLockKey(userId, normalizedScope),
                ct)
            .ConfigureAwait(false);
        try
        {
            await localLock.WaitAsync(ct).ConfigureAwait(false);
            return new MemoryWriteLease(localLock, () =>
                ReleaseMemoryDistributedLockAsync(distributedLease, CancellationToken.None));
        }
        catch
        {
            await ReleaseMemoryDistributedLockAsync(distributedLease, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static string BuildMemoryDistributedLockKey(string userId, string? projectId) =>
        $"agent_memory:{NormalizeLockSegment(userId)}:{NormalizeLockSegment(projectId ?? "<global>")}";

    private static string NormalizeLockSegment(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().Replace(' ', '_').Replace(':', '_');

    public async Task UpdateSessionMemoryAsync(string userId, string projectId, string sessionId, Dictionary<string, object> updates, CancellationToken ct = default)
    {
        if (updates.Keys.Any(memoryType => !memoryType.StartsWith("session.", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Session memory updates must use session.* memory types.", nameof(updates));
        }

        if (updates.Count == 0)
            return;

        await using var writeLock = await AcquireMemoryWriteLockAsync(userId, $"session_{sessionId}", ct).ConfigureAwait(false);

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

    public async Task RecordMemoryPromotionAsync(MemoryPromotionRecord record, CancellationToken ct = default)
    {
        _context.AgentMemoryPromotions.Add(new AgentMemoryPromotion
        {
            Id = Guid.NewGuid().ToString(),
            UserId = record.UserId,
            ProjectId = record.ProjectId,
            SessionId = record.SessionId,
            RunId = record.RunId,
            SourceScope = record.SourceScope,
            TargetScope = record.TargetScope,
            SourceMemoryKey = record.SourceMemoryKey,
            TargetMemoryKey = record.TargetMemoryKey,
            PromotionReason = record.PromotionReason,
            PayloadJson = string.IsNullOrWhiteSpace(record.PayloadJson) ? "{}" : record.PayloadJson,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(ct);
    }

    public async Task PromoteMemoryAsync(
        IReadOnlyList<MemoryPromotionRecord> records,
        IReadOnlyList<string> projectConstraints,
        CancellationToken ct = default)
    {
        if (records.Count == 0)
            return;

        var userId = records[0].UserId;
        var projectId = records[0].ProjectId;
        if (string.IsNullOrWhiteSpace(projectId) ||
            records.Any(record =>
                !string.Equals(record.UserId, userId, StringComparison.Ordinal) ||
                !string.Equals(record.ProjectId, projectId, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Memory promotion batch must belong to one user and project.", nameof(records));
        }

        await using var writeLock = await AcquireMemoryWriteLockAsync(userId, projectId, ct).ConfigureAwait(false);
        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
            transaction = await _context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            var memory = await _context.AgentMemories.FirstOrDefaultAsync(item =>
                item.UserId == userId &&
                item.ProjectId == projectId &&
                item.SessionId == null &&
                item.MemoryType == "project.constraints", ct).ConfigureAwait(false);
            var merged = ReadStringList(memory?.Content);
            foreach (var value in projectConstraints.Select(value => value.Trim()).Where(value => value.Length > 0))
            {
                if (!merged.Contains(value, StringComparer.OrdinalIgnoreCase))
                    merged.Add(value);
            }

            var now = DateTime.UtcNow;
            if (memory == null)
            {
                memory = new Data.Entities.AgentMemory
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = userId,
                    ProjectId = projectId,
                    MemoryType = "project.constraints",
                    MemoryKey = "constraints",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _context.AgentMemories.Add(memory);
            }
            memory.Content = JsonSerializer.Serialize(merged);
            memory.UpdatedAt = now;

            foreach (var record in records)
            {
                _context.AgentMemoryPromotions.Add(new AgentMemoryPromotion
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = record.UserId,
                    ProjectId = record.ProjectId,
                    SessionId = record.SessionId,
                    RunId = record.RunId,
                    SourceScope = record.SourceScope,
                    TargetScope = record.TargetScope,
                    SourceMemoryKey = record.SourceMemoryKey,
                    TargetMemoryKey = record.TargetMemoryKey,
                    PromotionReason = record.PromotionReason,
                    PayloadJson = string.IsNullOrWhiteSpace(record.PayloadJson) ? "{}" : record.PayloadJson,
                    CreatedAt = now,
                });
            }

            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
            if (transaction != null)
                await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync().ConfigureAwait(false);
        }

        await InvalidateCacheAsync(userId, projectId, "project.constraints").ConfigureAwait(false);
        await RecordMemoryWritesAsync(
            userId,
            projectId,
            null,
            new[] { "project.constraints" },
            "memory_promotion",
            new { records, projectConstraints },
            ct).ConfigureAwait(false);
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

    private async Task RecordMemoryReadAsync(
        string userId,
        string? projectId,
        string? sessionId,
        string? runId,
        string memoryScope,
        IReadOnlyList<string> memoryKeys,
        string consumer,
        CancellationToken ct)
    {
        _context.AgentMemoryReads.Add(new AgentMemoryRead
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = AgentMemoryScopes.ToStoreProjectId(projectId),
            SessionId = sessionId,
            RunId = string.IsNullOrWhiteSpace(runId) ? null : runId.Trim(),
            MemoryScope = memoryScope,
            MemoryKeysJson = JsonSerializer.Serialize(memoryKeys),
            SourceType = "memory_repository",
            Consumer = consumer,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(ct);
    }

    private async Task EnqueueProjectMemoryIndexOutboxAsync(
        string userId,
        string? projectId,
        IReadOnlyDictionary<string, object> updates,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        var memoryTypes = updates
            .Where(update => HasVectorizableProjectMemoryValue(update.Key, update.Value))
            .Select(update => update.Key)
            .ToList();

        if (memoryTypes.Count == 0)
        {
            return;
        }

        var rows = await _context.AgentMemories
            .AsNoTracking()
            .Where(memory =>
                memory.UserId == userId &&
                memory.ProjectId == projectId &&
                memory.SessionId == null &&
                memoryTypes.Contains(memory.MemoryType))
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            await _truthStore.EnqueueOutboxAsync(
                new EnqueueOutboxEventRequest(
                    UserId: userId,
                    ProjectId: projectId,
                    RuntimeRunId: null,
                    EventType: "index_memory_content",
                    AggregateType: "memory",
                    AggregateId: row.Id,
                    PayloadJson: "{}"),
                ct);
        }

        _logger.LogDebug("Queued {Count} project memory fields for vector indexing for user {UserId}, project {ProjectId}", rows.Count, userId, projectId);
    }

    private static bool IsVectorizedProjectMemoryType(string memoryType) =>
        memoryType is
            "project.long_term_goal" or
            "project.reader_promise" or
            "project.constraints" or
            "project.knowledge_inventory" or
            "project.used_trope_patterns";

    private static bool HasVectorizableProjectMemoryValue(string memoryType, object value)
    {
        if (!IsVectorizedProjectMemoryType(memoryType))
            return false;

        return memoryType switch
        {
            "project.long_term_goal" or "project.reader_promise" =>
                value is string text && !string.IsNullOrWhiteSpace(text),
            "project.constraints" or "project.used_trope_patterns" =>
                value is IEnumerable<string> items && items.Any(item => !string.IsNullOrWhiteSpace(item)),
            "project.knowledge_inventory" =>
                value is IEnumerable<KnowledgeInventoryItem> items && items.Any(),
            _ => false
        };
    }

    private static string BuildSessionCacheKey(string userId, string sessionId) =>
        $"memory:session:{userId}:{sessionId}";

    private static readonly string[] ProjectMemoryKeys =
    {
        "project.long_term_goal",
        "project.reader_promise",
        "project.constraints",
        "project.referenced_knowledge_ids",
        "project.imported_knowledge_ids",
        "project.knowledge_inventory",
        "project.used_trope_patterns"
    };

    private static readonly string[] SessionMemoryKeys =
    {
        "session.current_goal",
        "session.open_questions",
        "session.short_term_preferences",
        "session.recent_observations",
        "session.pending_tool_name",
        "session.last_intent"
    };

    private static readonly string[] AuthorMemoryKeys =
    {
        "author.display_name",
        "author.style_likes",
        "author.style_dislikes",
        "author.confirmation_tolerance",
        "author.genre_habits",
        "author.favorite_knowledge_ids"
    };

    private static readonly string[] ExecutionMemoryKeys =
    {
        "execution.tool_failures",
        "execution.repeated_blockers",
        "execution.successful_repairs",
        "execution.knowledge_processing_failures"
    };

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

    private sealed class MemoryWriteLease : IAsyncDisposable
    {
        private readonly SemaphoreSlim _localLock;
        private readonly Func<Task> _releaseDistributed;
        private int _released;

        public MemoryWriteLease(SemaphoreSlim localLock, Func<Task> releaseDistributed)
        {
            _localLock = localLock;
            _releaseDistributed = releaseDistributed;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            _localLock.Release();
            await _releaseDistributed().ConfigureAwait(false);
        }
    }
}

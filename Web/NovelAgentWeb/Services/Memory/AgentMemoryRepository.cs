using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public class AgentMemoryRepository : IAgentMemoryRepository
{
    private readonly NovelAgentDbContext _context;
    private readonly IDistributedCacheService _redisCache;
    private readonly IMemoryCacheService _memoryCache;
    private readonly ILogger<AgentMemoryRepository> _logger;

    private static readonly TimeSpan MemoryCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RedisCacheDuration = TimeSpan.FromMinutes(10);

    public AgentMemoryRepository(
        NovelAgentDbContext context,
        IDistributedCacheService redisCache,
        IMemoryCacheService memoryCache,
        ILogger<AgentMemoryRepository> logger)
    {
        _context = context;
        _redisCache = redisCache;
        _memoryCache = memoryCache;
        _logger = logger;
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
                    .Where(m => m.UserId == userId && m.ProjectId == projectId && m.MemoryType.StartsWith("project."))
                    .ToListAsync(ct);

                var memory = new ProjectMemory
                {
                    LongTermGoal = GetField<string>(rows, "project.long_term_goal"),
                    ReaderPromise = GetField<string>(rows, "project.reader_promise"),
                    Constraints = GetField<List<string>>(rows, "project.constraints") ?? new(),
                    UnresolvedThreads = GetField<List<string>>(rows, "project.unresolved_threads") ?? new()
                };

                await _redisCache.SetAsync(cacheKey, memory, RedisCacheDuration, ct);

                _logger.LogDebug("ProjectMemory loaded from database for user {UserId}, project {ProjectId}", userId, projectId);
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
                    .Where(m => m.UserId == userId && m.ProjectId == null && m.MemoryType.StartsWith("author."))
                    .ToListAsync(ct);

                var memory = new AuthorMemory
                {
                    StyleLikes = GetField<List<string>>(rows, "author.style_likes") ?? new(),
                    StyleDislikes = GetField<List<string>>(rows, "author.style_dislikes") ?? new(),
                    ConfirmationTolerance = GetField<string>(rows, "author.confirmation_tolerance"),
                    GenreHabits = GetField<List<string>>(rows, "author.genre_habits") ?? new()
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
        var cacheKey = $"memory:execution:{userId}:{projectId}";

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
                    .Where(m => m.UserId == userId && m.ProjectId == projectId && m.MemoryType.StartsWith("execution."))
                    .ToListAsync(ct);

                var memory = new ExecutionMemory
                {
                    ToolFailurePatterns = GetField<List<string>>(rows, "execution.tool_failures") ?? new(),
                    RepeatedBlockers = GetField<List<string>>(rows, "execution.repeated_blockers") ?? new(),
                    SuccessfulRepairNotes = GetField<List<string>>(rows, "execution.successful_repairs") ?? new()
                };

                await _redisCache.SetAsync(cacheKey, memory, RedisCacheDuration, ct);

                _logger.LogDebug("ExecutionMemory loaded from database for user {UserId}, project {ProjectId}", userId, projectId);
                return memory;
            },
            MemoryCacheDuration,
            ct);
    }

    public Task UpdateFieldAsync(string userId, string? projectId, string memoryType, object value, CancellationToken ct = default)
    {
        throw new NotImplementedException("Will be implemented in Task 7");
    }

    public Task UpdateMemoryAsync(string userId, string? projectId, Dictionary<string, object> updates, CancellationToken ct = default)
    {
        throw new NotImplementedException("Will be implemented in Task 7");
    }

    private static T? GetField<T>(List<AgentMemory> rows, string memoryType)
    {
        var row = rows.FirstOrDefault(r => r.MemoryType == memoryType);
        if (row == null || string.IsNullOrEmpty(row.Content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(row.Content);
    }
}

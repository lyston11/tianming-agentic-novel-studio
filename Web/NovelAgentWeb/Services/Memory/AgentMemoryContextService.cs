using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public class AgentMemoryContextService : IAgentMemoryContextService
{
    private static readonly TimeSpan ContextCacheTtl = TimeSpan.FromMinutes(5);

    private readonly IChatHistoryRepository _chatHistory;
    private readonly IAgentMemoryRepository _memoryRepository;
    private readonly IDistributedCacheService? _redisCache;
    private readonly IMemoryCacheService? _memoryCache;
    private readonly IAgentMemoryVersionService? _versions;

    public AgentMemoryContextService(
        IChatHistoryRepository chatHistory,
        IAgentMemoryRepository memoryRepository,
        IDistributedCacheService? redisCache = null,
        IMemoryCacheService? memoryCache = null,
        IAgentMemoryVersionService? versions = null)
    {
        _chatHistory = chatHistory;
        _memoryRepository = memoryRepository;
        _redisCache = redisCache;
        _memoryCache = memoryCache;
        _versions = versions;
    }

    public async Task<AgentMemoryContextDto> BuildAsync(
        string userId,
        string projectId,
        string sessionId,
        CancellationToken ct = default)
    {
        if (_versions != null)
        {
            var version = await _versions.GetCombinedVersionAsync(userId, projectId, sessionId, ct)
                .ConfigureAwait(false);
            var cacheKey = AgentMemoryKeys.MemoryContext(userId, sessionId, projectId, version);

            var memoryCached = _memoryCache?.Get<AgentMemoryContextDto>(cacheKey);
            if (memoryCached != null)
                return memoryCached;

            if (_redisCache != null)
            {
                var redisCached = await _redisCache.GetAsync<AgentMemoryContextDto>(cacheKey, ct)
                    .ConfigureAwait(false);
                if (redisCached != null)
                {
                    _memoryCache?.Set(cacheKey, redisCached, ContextCacheTtl);
                    return redisCached;
                }
            }

            var composed = await ComposeAsync(userId, projectId, sessionId, ct).ConfigureAwait(false);
            _memoryCache?.Set(cacheKey, composed, ContextCacheTtl);
            if (_redisCache != null)
                await _redisCache.SetAsync(cacheKey, composed, ContextCacheTtl, ct).ConfigureAwait(false);
            return composed;
        }

        return await ComposeAsync(userId, projectId, sessionId, ct).ConfigureAwait(false);
    }

    private async Task<AgentMemoryContextDto> ComposeAsync(
        string userId,
        string projectId,
        string sessionId,
        CancellationToken ct)
    {
        var chat = await _chatHistory.GetPromptWindowAsync(userId, projectId, sessionId, ct);
        var session = await _memoryRepository.GetSessionMemoryAsync(userId, projectId, sessionId, ct);
        var project = await _memoryRepository.GetProjectMemoryAsync(userId, projectId, ct);
        var author = await _memoryRepository.GetAuthorMemoryAsync(userId, ct);
        var execution = await _memoryRepository.GetExecutionMemoryAsync(userId, projectId, ct);

        return new AgentMemoryContextDto(
            new ChatMemoryContext(chat.MetaSummary, chat.Summaries, chat.RecentMessages),
            session,
            project,
            author,
            execution);
    }
}

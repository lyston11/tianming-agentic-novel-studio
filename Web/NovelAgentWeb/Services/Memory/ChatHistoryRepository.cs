using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.Memory;

    public class ChatHistoryRepository : IChatHistoryRepository
    {
        private static readonly TimeSpan HotWindowTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan SummaryCacheTtl = TimeSpan.FromMinutes(10);
        private const int HotWindowSize = 20;
        private const int MaxAppendAttempts = 3;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SessionLocks = new(StringComparer.Ordinal);

    private readonly NovelAgentDbContext _context;
    private readonly IDistributedCacheService _redisCache;
    private readonly IMemoryCacheService _memoryCache;
    private readonly ILogger<ChatHistoryRepository> _logger;
    private readonly IAgentMemoryVersionService? _versions;

    public ChatHistoryRepository(
        NovelAgentDbContext context,
        IDistributedCacheService redisCache,
        IMemoryCacheService memoryCache,
        ILogger<ChatHistoryRepository> logger,
        IAgentMemoryVersionService? versions = null)
    {
        _context = context;
        _redisCache = redisCache;
        _memoryCache = memoryCache;
        _logger = logger;
        _versions = versions;
    }

    public async Task AppendAsync(
        string userId,
        string? projectId,
        string sessionId,
        string role,
        string content,
        CancellationToken ct = default)
    {
        var trimmed = content.Trim();
        var sessionLock = SessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaxAppendAttempts; attempt++)
            {
                try
                {
                    await EnsureSessionExistsAsync(userId, projectId, sessionId, ct).ConfigureAwait(false);
                    await AppendTurnOnceAsync(userId, projectId, sessionId, role, trimmed, ct);
                    await WriteHotWindowAsync(userId, projectId, sessionId, ct);
                    await BumpChatVersionAsync(userId, projectId, sessionId, ct).ConfigureAwait(false);
                    return;
                }
                catch (DbUpdateException) when (attempt < MaxAppendAttempts)
                {
                    _context.ChangeTracker.Clear();
                    _logger.LogWarning("Retrying chat turn append after unique index conflict for session {SessionId}", sessionId);
                }
            }

            throw new DbUpdateException($"Failed to append chat turn for session {sessionId} after {MaxAppendAttempts} attempts.");
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task SaveSummaryAsync(
        string userId,
        string? projectId,
        string sessionId,
        int startTurn,
        int endTurn,
        string summaryType,
        string content,
        IReadOnlyList<string> keyDecisions,
        CancellationToken ct = default)
    {
        var normalizedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId;
        var normalizedSummaryType = string.IsNullOrWhiteSpace(summaryType) ? "summary" : summaryType.Trim();
        var trimmedContent = content.Trim();
        var keyDecisionsJson = JsonSerializer.Serialize(keyDecisions);

        var existing = await FindSummaryAsync(userId, normalizedProjectId, sessionId, normalizedSummaryType, startTurn, endTurn, ct);

        if (existing != null)
        {
            UpdateSummary(existing, trimmedContent, keyDecisionsJson);
            await _context.SaveChangesAsync(ct);
            await InvalidateSummaryCacheAsync(userId, normalizedProjectId, sessionId, ct);
            await BumpChatVersionAsync(userId, normalizedProjectId, sessionId, ct).ConfigureAwait(false);
            return;
        }

        _context.AgentChatSummaries.Add(new AgentChatSummary
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = normalizedProjectId,
            SessionId = sessionId,
            StartTurn = startTurn,
            EndTurn = endTurn,
            SummaryType = normalizedSummaryType,
            Content = trimmedContent,
            KeyDecisionsJson = keyDecisionsJson,
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            var concurrentExisting = await FindSummaryAsync(userId, normalizedProjectId, sessionId, normalizedSummaryType, startTurn, endTurn, ct);
            if (concurrentExisting == null)
            {
                throw;
            }

            UpdateSummary(concurrentExisting, trimmedContent, keyDecisionsJson);
            await _context.SaveChangesAsync(ct);
        }

        await InvalidateSummaryCacheAsync(userId, normalizedProjectId, sessionId, ct);
        await BumpChatVersionAsync(userId, normalizedProjectId, sessionId, ct).ConfigureAwait(false);
    }

    public async Task<ChatPromptWindowDto> GetPromptWindowAsync(
        string userId,
        string? projectId,
        string sessionId,
        CancellationToken ct = default)
    {
        var normalizedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId;
        var summaryCache = await LoadSummaryCacheAsync(userId, normalizedProjectId, sessionId, ct)
            .ConfigureAwait(false);
        var recentMessages = await LoadRecentTurnsFromHotCacheAsync(userId, normalizedProjectId, sessionId, ct)
            .ConfigureAwait(false);

        return new ChatPromptWindowDto(summaryCache.MetaSummary, summaryCache.Summaries, recentMessages);
    }

    public async Task<IReadOnlyList<ChatHistoryTurnDto>> GetHotWindowAsync(
        string userId,
        string? projectId,
        string sessionId,
        CancellationToken ct = default) =>
        await LoadRecentTurnsFromHotCacheAsync(
                userId,
                string.IsNullOrWhiteSpace(projectId) ? null : projectId,
                sessionId,
                ct)
            .ConfigureAwait(false);

    private async Task<ChatPromptSummaryCache> LoadSummaryCacheAsync(
        string userId,
        string? normalizedProjectId,
        string sessionId,
        CancellationToken ct)
    {
        var cacheKey = BuildSummaryCacheKey(userId, normalizedProjectId, sessionId);
        var memoryCached = _memoryCache.Get<ChatPromptSummaryCache>(cacheKey);
        if (memoryCached != null)
            return memoryCached;

        var redisCached = await _redisCache.GetAsync<ChatPromptSummaryCache>(cacheKey, ct)
            .ConfigureAwait(false);
        if (redisCached != null)
        {
            _memoryCache.Set(cacheKey, redisCached, SummaryCacheTtl);
            return redisCached;
        }

        var summariesQuery = _context.AgentChatSummaries
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.ProjectId == normalizedProjectId && s.SessionId == sessionId);

        var metaSummary = await summariesQuery
            .Where(s => s.SummaryType == "meta")
            .OrderByDescending(s => s.EndTurn)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => s.Content)
            .FirstOrDefaultAsync(ct);

        var summaryRows = await summariesQuery
            .Where(s => s.SummaryType != "meta")
            .OrderBy(s => s.StartTurn)
            .ThenBy(s => s.EndTurn)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);
        var summaries = summaryRows
            .Select(s => new ChatHistorySummaryDto(
                s.StartTurn,
                s.EndTurn,
                s.SummaryType,
                s.Content,
                DeserializeKeyDecisions(s.KeyDecisionsJson)))
            .ToList();

        var cache = new ChatPromptSummaryCache(metaSummary, summaries);
        _memoryCache.Set(cacheKey, cache, SummaryCacheTtl);
        await _redisCache.SetAsync(cacheKey, cache, SummaryCacheTtl, ct).ConfigureAwait(false);
        return cache;
    }

    private async Task AppendTurnOnceAsync(
        string userId,
        string? projectId,
        string sessionId,
        string role,
        string trimmed,
        CancellationToken ct)
    {
        var nextTurnIndex = await _context.AgentChatTurns
            .Where(t => t.SessionId == sessionId)
            .Select(t => (int?)t.TurnIndex)
            .MaxAsync(ct) ?? 0;

        _context.AgentChatTurns.Add(new AgentChatTurn
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            SessionId = sessionId,
            TurnIndex = nextTurnIndex + 1,
            Role = role.Trim(),
            Content = trimmed,
            TokenCount = EstimateTokenCount(trimmed),
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(ct);
    }

    private async Task EnsureSessionExistsAsync(string userId, string? projectId, string sessionId, CancellationToken ct)
    {
        var exists = await _context.AgentSessions
            .AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            .ConfigureAwait(false);
        if (exists)
            return;

        _context.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            UserId = userId,
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            Title = "新会话",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            exists = await _context.AgentSessions
                .AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct)
                .ConfigureAwait(false);
            if (!exists)
            {
                throw;
            }
        }
    }

    private Task<AgentChatSummary?> FindSummaryAsync(
        string userId,
        string? projectId,
        string sessionId,
        string summaryType,
        int startTurn,
        int endTurn,
        CancellationToken ct) =>
        _context.AgentChatSummaries
            .FirstOrDefaultAsync(s =>
                s.UserId == userId &&
                s.ProjectId == projectId &&
                s.SessionId == sessionId &&
                s.SummaryType == summaryType &&
                s.StartTurn == startTurn &&
                s.EndTurn == endTurn,
                ct);

    private static void UpdateSummary(AgentChatSummary summary, string content, string keyDecisionsJson)
    {
        summary.Content = content;
        summary.KeyDecisionsJson = keyDecisionsJson;
        summary.CreatedAt = DateTime.UtcNow;
    }

    private async Task WriteHotWindowAsync(string userId, string? projectId, string sessionId, CancellationToken ct)
    {
        var normalizedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId;
        var hotWindow = await LoadRecentTurnsAsync(userId, normalizedProjectId, sessionId, ct);
        var key = AgentMemoryKeys.ChatHot(userId, sessionId, normalizedProjectId);

        await _redisCache.SetAsync(key, hotWindow, HotWindowTtl, ct);
        _memoryCache.Set(key, hotWindow, HotWindowTtl);

        _logger.LogDebug("Updated chat hot window for user {UserId}, session {SessionId}", userId, sessionId);
    }

    private async Task<List<ChatHistoryTurnDto>> LoadRecentTurnsFromHotCacheAsync(
        string userId,
        string? projectId,
        string sessionId,
        CancellationToken ct)
    {
        var key = AgentMemoryKeys.ChatHot(userId, sessionId, projectId);
        var memoryCached = _memoryCache.Get<List<ChatHistoryTurnDto>>(key);
        if (memoryCached != null)
            return memoryCached;

        var redisCached = await _redisCache.GetAsync<List<ChatHistoryTurnDto>>(key, ct)
            .ConfigureAwait(false);
        if (redisCached != null)
        {
            _memoryCache.Set(key, redisCached, HotWindowTtl);
            return redisCached;
        }

        var turns = await LoadRecentTurnsAsync(userId, projectId, sessionId, ct).ConfigureAwait(false);
        _memoryCache.Set(key, turns, HotWindowTtl);
        await _redisCache.SetAsync(key, turns, HotWindowTtl, ct).ConfigureAwait(false);
        return turns;
    }

    private async Task InvalidateSummaryCacheAsync(
        string userId,
        string? normalizedProjectId,
        string sessionId,
        CancellationToken ct)
    {
        var cacheKey = BuildSummaryCacheKey(userId, normalizedProjectId, sessionId);
        _memoryCache.Remove(cacheKey);
        await _redisCache.RemoveAsync(cacheKey, ct).ConfigureAwait(false);
    }

    private Task BumpChatVersionAsync(string userId, string? projectId, string sessionId, CancellationToken ct) =>
        _versions == null
            ? Task.CompletedTask
            : _versions.BumpAsync(userId, string.IsNullOrWhiteSpace(projectId) ? null : projectId, sessionId, "chat", ct);

    private static string BuildSummaryCacheKey(string userId, string? projectId, string sessionId) =>
        $"chat:{userId}:{sessionId}:{(string.IsNullOrWhiteSpace(projectId) ? "*" : projectId)}:summaries";

    private async Task<List<ChatHistoryTurnDto>> LoadRecentTurnsAsync(
        string userId,
        string? projectId,
        string sessionId,
        CancellationToken ct)
    {
        var normalizedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId;
        var turns = await _context.AgentChatTurns
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.ProjectId == normalizedProjectId && t.SessionId == sessionId)
            .OrderByDescending(t => t.TurnIndex)
            .Take(HotWindowSize)
            .OrderBy(t => t.TurnIndex)
            .Select(t => new ChatHistoryTurnDto(t.Role, t.Content, t.CreatedAt))
            .ToListAsync(ct);

        return turns;
    }

    private static IReadOnlyList<string> DeserializeKeyDecisions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            var decisions = JsonSerializer.Deserialize<List<string>>(json);
            return decisions ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static int EstimateTokenCount(string content) =>
        string.IsNullOrWhiteSpace(content) ? 0 : Math.Max(1, content.Length / 2);

    private sealed record ChatPromptSummaryCache(
        string? MetaSummary,
        List<ChatHistorySummaryDto> Summaries);
}

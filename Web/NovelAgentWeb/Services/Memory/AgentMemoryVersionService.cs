using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public class AgentMemoryVersionService : IAgentMemoryVersionService
{
    private readonly NovelAgentDbContext _db;
    private readonly IDistributedCacheService _distributedCache;
    private readonly IMemoryCacheService _memoryCache;

    public AgentMemoryVersionService(
        NovelAgentDbContext db,
        IDistributedCacheService distributedCache,
        IMemoryCacheService memoryCache)
    {
        _db = db;
        _distributedCache = distributedCache;
        _memoryCache = memoryCache;
    }

    public async Task<long> BumpAsync(string userId, string? projectId, string? sessionId, string scope, CancellationToken ct = default)
    {
        var row = await _db.AgentMemoryVersions
            .FirstOrDefaultAsync(v =>
                v.UserId == userId &&
                v.ProjectId == projectId &&
                v.SessionId == sessionId &&
                v.Scope == scope, ct);

        if (row == null)
        {
            row = new AgentMemoryVersion
            {
                UserId = userId,
                ProjectId = projectId,
                SessionId = sessionId,
                Scope = scope,
                Version = 0
            };
            _db.AgentMemoryVersions.Add(row);
        }

        row.Version++;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (projectId != null && sessionId != null)
        {
            await InvalidateContextCachesAsync(userId, projectId, sessionId, ct);
        }

        return row.Version;
    }

    public async Task<string> GetCombinedVersionAsync(string userId, string? projectId, string? sessionId, CancellationToken ct = default)
    {
        var rows = await _db.AgentMemoryVersions
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .Where(v => v.ProjectId == projectId || v.ProjectId == null)
            .Where(v => v.SessionId == sessionId || v.SessionId == null)
            .OrderBy(v => v.Scope)
            .ThenBy(v => v.ProjectId)
            .ThenBy(v => v.SessionId)
            .ToListAsync(ct);

        return rows.Count == 0
            ? "none=0"
            : string.Join("|", rows.Select(r => $"{r.Scope}:{r.ProjectId ?? "*"}:{r.SessionId ?? "*"}={r.Version}"));
    }

    private async Task InvalidateContextCachesAsync(string userId, string projectId, string sessionId, CancellationToken ct)
    {
        var memoryContextPrefix = $"memory-context:{userId}:{sessionId}:{projectId}";
        var toolCachePrefix = $"toolcache:{userId}:{sessionId}:{projectId}";

        _memoryCache.RemoveByPrefix(memoryContextPrefix);
        _memoryCache.RemoveByPrefix(toolCachePrefix);

        // Distributed cache currently has no prefix delete API, so remove the prefix keys exactly.
        await _distributedCache.RemoveAsync(memoryContextPrefix, ct);
        await _distributedCache.RemoveAsync(toolCachePrefix, ct);
    }
}

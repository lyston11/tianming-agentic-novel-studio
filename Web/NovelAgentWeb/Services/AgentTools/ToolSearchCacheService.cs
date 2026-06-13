using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public class ToolSearchCacheService : IToolSearchCacheService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IDistributedCacheService _redis;
    private readonly IMemoryCacheService _memory;
    private readonly IAgentMemoryVersionService _versions;

    public ToolSearchCacheService(
        IDistributedCacheService redis,
        IMemoryCacheService memory,
        IAgentMemoryVersionService versions)
    {
        _redis = redis;
        _memory = memory;
        _versions = versions;
    }

    public async Task<IReadOnlyList<ToolSchema>?> GetAsync(
        AgentSession session,
        string phase,
        CancellationToken ct = default)
    {
        var version = await _versions.GetCombinedVersionAsync(
            session.UserId,
            NullIfEmpty(session.ActiveProjectId),
            session.SessionId,
            ct);

        if (!string.Equals(session.ToolSearchCacheVersion, version, StringComparison.Ordinal))
            return null;

        if (IsValidSessionSnapshot(session, phase))
            return session.DiscoveredTools;

        var cacheKey = BuildCacheKey(session, phase);
        var cached = _memory.Get<ToolSearchCacheSnapshot>(cacheKey) ??
                     await _redis.GetAsync<ToolSearchCacheSnapshot>(cacheKey, ct);

        if (cached == null ||
            cached.ExpiresAt <= DateTime.UtcNow ||
            !string.Equals(cached.Version, version, StringComparison.Ordinal) ||
            !string.Equals(cached.Phase, phase, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        ApplySnapshot(session, cached);
        return session.DiscoveredTools;
    }

    public async Task SaveAsync(
        AgentSession session,
        string phase,
        IReadOnlyList<ToolSchema> tools,
        CancellationToken ct = default)
    {
        var version = await _versions.GetCombinedVersionAsync(
            session.UserId,
            NullIfEmpty(session.ActiveProjectId),
            session.SessionId,
            ct);
        var now = DateTime.UtcNow;
        var snapshot = new ToolSearchCacheSnapshot
        {
            Phase = phase,
            Tools = tools.ToList(),
            Version = version,
            CachedAt = now,
            ExpiresAt = now.Add(CacheTtl)
        };

        ApplySnapshot(session, snapshot);

        var cacheKey = BuildCacheKey(session, phase);
        _memory.Set(cacheKey, snapshot, CacheTtl);
        await _redis.SetAsync(cacheKey, snapshot, CacheTtl, ct);
    }

    private static bool IsValidSessionSnapshot(AgentSession session, string phase)
    {
        return !string.IsNullOrWhiteSpace(session.DiscoveredPhase) &&
               session.DiscoveredTools.Count > 0 &&
               string.Equals(session.DiscoveredPhase, phase, StringComparison.OrdinalIgnoreCase) &&
               session.LastToolSearchAt != null &&
               DateTime.UtcNow - session.LastToolSearchAt.Value <= CacheTtl;
    }

    private static void ApplySnapshot(AgentSession session, ToolSearchCacheSnapshot snapshot)
    {
        session.DiscoveredPhase = snapshot.Phase;
        session.DiscoveredTools = snapshot.Tools.ToList();
        session.LastToolSearchAt = snapshot.CachedAt;
        session.ToolSearchCacheVersion = snapshot.Version;
    }

    private static string BuildCacheKey(AgentSession session, string phase) =>
        $"toolcache:{session.UserId}:{session.SessionId}:{NullIfEmpty(session.ActiveProjectId) ?? "*"}:{phase}";

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class ToolSearchCacheSnapshot
    {
        public string Phase { get; set; } = string.Empty;
        public List<ToolSchema> Tools { get; set; } = new();
        public string Version { get; set; } = string.Empty;
        public DateTime CachedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}

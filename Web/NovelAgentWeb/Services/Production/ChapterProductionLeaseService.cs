using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ChapterProductionLeaseService : IChapterProductionLeaseService
{
    private static readonly TimeSpan ChapterProductionLockTtl = TimeSpan.FromMinutes(15);

    private readonly IDistributedLockService _locks;

    public ChapterProductionLeaseService(IDistributedLockService locks)
    {
        _locks = locks;
    }

    public async Task<ChapterProductionLease?> TryAcquireAsync(
        string userId,
        string projectId,
        string chapterId,
        string runtimeRunId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(chapterId))
            return null;

        var key = BuildKey(projectId, chapterId);
        var owner = BuildOwner(userId, runtimeRunId);
        var lease = await _locks.TryAcquireAsync(key, ChapterProductionLockTtl, owner, cancellationToken)
            .ConfigureAwait(false);
        return lease == null
            ? null
            : new ChapterProductionLease(_locks, lease, key);
    }

    internal static string BuildKey(string projectId, string chapterId) =>
        $"agent_production:chapter:{Normalize(projectId)}:{Normalize(chapterId)}";

    private static string BuildOwner(string userId, string runtimeRunId) =>
        $"{Normalize(userId)}:{Normalize(runtimeRunId)}";

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().Replace(' ', '_').Replace(':', '_');
}

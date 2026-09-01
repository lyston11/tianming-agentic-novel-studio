using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IChapterProductionLeaseService
{
    Task<ChapterProductionLease?> TryAcquireAsync(
        string userId,
        string projectId,
        string chapterId,
        string runtimeRunId,
        CancellationToken cancellationToken = default);
}

public sealed class ChapterProductionLease : IAsyncDisposable
{
    private readonly IDistributedLockService _locks;
    private readonly DistributedLockLease _lease;

    public ChapterProductionLease(
        IDistributedLockService locks,
        DistributedLockLease lease,
        string key)
    {
        _locks = locks;
        _lease = lease;
        Key = key;
    }

    public string Key { get; }

    public ValueTask DisposeAsync() =>
        new(_locks.ReleaseAsync(_lease));
}

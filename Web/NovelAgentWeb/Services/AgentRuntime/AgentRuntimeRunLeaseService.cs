using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public interface IAgentRuntimeRunLeaseService
{
    Task<AgentRuntimeRunLease?> TryAcquireAsync(string runtimeRunId, CancellationToken ct = default);
}

public sealed class AgentRuntimeRunLease : IAsyncDisposable
{
    private readonly IDistributedLockService _locks;
    private DistributedLockLease _lease;
    private CancellationTokenSource? _renewalCts;
    private Task? _renewalTask;
    private bool _released;

    public AgentRuntimeRunLease(
        string runtimeRunId,
        DistributedLockLease lease,
        IDistributedLockService locks)
    {
        RuntimeRunId = runtimeRunId;
        _lease = lease;
        _locks = locks;
    }

    public string RuntimeRunId { get; }

    public string Token => _lease.Token;

    public DateTime ExpiresAt => _lease.ExpiresAt;

    public async Task<bool> RenewAsync(TimeSpan ttl, CancellationToken ct = default)
    {
        if (_released)
            return false;

        var renewed = await _locks.ExtendAsync(_lease, ttl, ct).ConfigureAwait(false);
        if (renewed == null)
            return false;

        _lease = renewed;
        return true;
    }

    public void StartAutoRenewal(
        TimeSpan interval,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        if (_released || _renewalTask != null)
            return;

        _renewalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _renewalTask = RenewLoopAsync(
            interval <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : interval,
            ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : ttl,
            _renewalCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_released)
            return;

        _released = true;
        if (_renewalCts != null)
        {
            await _renewalCts.CancelAsync().ConfigureAwait(false);
            if (_renewalTask != null)
            {
                try
                {
                    await _renewalTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            _renewalCts.Dispose();
        }

        await _locks.ReleaseAsync(_lease).ConfigureAwait(false);
    }

    private async Task RenewLoopAsync(TimeSpan interval, TimeSpan ttl, CancellationToken ct)
    {
        if (!await RenewAsync(ttl, ct).ConfigureAwait(false))
            return;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct).ConfigureAwait(false);
            if (!await RenewAsync(ttl, ct).ConfigureAwait(false))
                return;
        }
    }
}

public sealed class AgentRuntimeRunLeaseService : IAgentRuntimeRunLeaseService
{
    private static readonly TimeSpan RuntimeRunLockTtl = TimeSpan.FromMinutes(10);
    private readonly IDistributedLockService _locks;
    private readonly string _owner;

    public AgentRuntimeRunLeaseService(IDistributedLockService locks)
    {
        _locks = locks;
        _owner = $"agent-runtime-worker:{Environment.MachineName}:{Guid.NewGuid():N}";
    }

    public async Task<AgentRuntimeRunLease?> TryAcquireAsync(string runtimeRunId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(runtimeRunId))
            return null;

        var normalizedRunId = runtimeRunId.Trim();
        var lease = await _locks
            .TryAcquireAsync(BuildRuntimeRunLockKey(normalizedRunId), RuntimeRunLockTtl, _owner, ct)
            .ConfigureAwait(false);
        return lease == null
            ? null
            : new AgentRuntimeRunLease(normalizedRunId, lease, _locks);
    }

    private static string BuildRuntimeRunLockKey(string runtimeRunId) =>
        $"agent_runtime:lock:{runtimeRunId}";
}

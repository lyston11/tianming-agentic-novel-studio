using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public sealed class LegacyRuntimeAuditReader : ILegacyRuntimeAuditReader
{
    private readonly NovelAgentDbContext _db;

    public LegacyRuntimeAuditReader(NovelAgentDbContext db)
    {
        _db = db;
    }

    public Task<AgentRuntimeRun?> TryGetAsync(
        string runtimeRunId,
        CancellationToken cancellationToken = default) =>
        _db.AgentRuntimeRuns.AsNoTracking()
            .SingleOrDefaultAsync(run => run.Id == runtimeRunId, cancellationToken);

    public async Task<AgentRuntimeActiveRunState?> TryGetActiveStateAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var run = await _db.AgentRuntimeRuns.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.SessionId == sessionId &&
                AgentRuntimeRunStatus.Active.Contains(item.Status))
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return run == null
            ? null
            : new AgentRuntimeActiveRunState(run, run.UpdatedAt, FromDistributedCache: false);
    }
}

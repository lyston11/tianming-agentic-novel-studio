using System.Collections.Concurrent;
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public sealed class WorkspaceViolationTracker : IWorkspaceViolationTracker, IDisposable
{
    private readonly ConcurrentDictionary<Guid, WorkspaceViolation> _violations = new();
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _defaultRetention = TimeSpan.FromHours(24);

    public WorkspaceViolationTracker()
    {
        _cleanupTimer = new Timer(
            _ => ClearOldViolations(_defaultRetention),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    public void TrackViolation(WorkspaceViolation violation)
    {
        if (violation == null) return;
        _violations[violation.Id] = violation;
    }

    public IEnumerable<WorkspaceViolation> GetViolations(
        ViolationSeverity? severity = null,
        DateTime? since = null,
        string? userId = null,
        string? projectId = null)
    {
        var query = _violations.Values.AsEnumerable();

        if (severity.HasValue)
            query = query.Where(v => v.Severity == severity.Value);

        if (since.HasValue)
            query = query.Where(v => v.Timestamp >= since.Value);

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(v => v.UserId == userId);

        if (!string.IsNullOrEmpty(projectId))
            query = query.Where(v => v.ProjectId == projectId);

        return query.OrderByDescending(v => v.Timestamp).ToList();
    }

    public void ClearOldViolations(TimeSpan retentionPeriod)
    {
        var cutoffTime = DateTime.UtcNow - retentionPeriod;
        var oldViolations = _violations
            .Where(kvp => kvp.Value.Timestamp < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in oldViolations)
        {
            _violations.TryRemove(id, out _);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

using System.Collections.Concurrent;
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public sealed class WorkspaceViolationTracker : IWorkspaceViolationTracker, IDisposable
{
    private readonly ConcurrentDictionary<Guid, WorkspaceViolation> _violations = new();
    private readonly ConcurrentDictionary<string, WorkspaceViolation> _violationsByKey = new();
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

    public WorkspaceViolation RecordViolation(
        string userId,
        string projectId,
        string requestId,
        string path,
        ViolationType type,
        string message)
    {
        var key = $"{userId}:{projectId}:{type}";

        // Check if this is a repeat violation
        if (_violationsByKey.TryGetValue(key, out var existing))
        {
            // Increment count on existing violation
            existing.Count++;
            existing.Timestamp = DateTime.UtcNow;
            existing.RequestId = requestId;
            existing.Endpoint = path;
            return existing;
        }

        // Create new violation
        var severity = type switch
        {
            ViolationType.WorkspaceNotAcquired => ViolationSeverity.High,
            ViolationType.WorkspaceExpired => ViolationSeverity.High,
            ViolationType.WorkspaceMismatch => ViolationSeverity.Critical,
            ViolationType.ConcurrentAccessDetected => ViolationSeverity.Medium,
            _ => ViolationSeverity.Low
        };

        var violation = new WorkspaceViolation
        {
            Type = type,
            Severity = severity,
            UserId = userId,
            ProjectId = projectId,
            RequestId = requestId,
            Endpoint = path,
            Method = "GET", // Could be passed as parameter
            Message = message,
            Count = 1
        };

        _violations[violation.Id] = violation;
        _violationsByKey[key] = violation;

        return violation;
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

    public List<WorkspaceViolation> GetViolationsForKey(string key)
    {
        if (_violationsByKey.TryGetValue(key, out var violation))
        {
            return new List<WorkspaceViolation> { violation };
        }
        return new List<WorkspaceViolation>();
    }

    public void ClearViolation(string key)
    {
        if (_violationsByKey.TryRemove(key, out var violation))
        {
            _violations.TryRemove(violation.Id, out _);
        }
    }

    public void ClearAll()
    {
        _violations.Clear();
        _violationsByKey.Clear();
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
            if (_violations.TryRemove(id, out var violation))
            {
                _violationsByKey.TryRemove(violation.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

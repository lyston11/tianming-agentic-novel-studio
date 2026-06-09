using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public interface IWorkspaceViolationTracker
{
    /// <summary>
    /// Record a new violation with explicit parameters.
    /// Returns the violation record with updated count if this is a repeat violation.
    /// </summary>
    WorkspaceViolation RecordViolation(
        string userId,
        string projectId,
        string requestId,
        string path,
        ViolationType type,
        string message);

    /// <summary>
    /// Get all violations matching the filter criteria.
    /// </summary>
    IEnumerable<WorkspaceViolation> GetViolations(
        ViolationSeverity? severity = null,
        DateTime? since = null,
        string? userId = null,
        string? projectId = null);

    /// <summary>
    /// Get violations grouped by key (userId:projectId:type).
    /// </summary>
    List<WorkspaceViolation> GetViolationsForKey(string key);

    /// <summary>
    /// Clear violations for a specific key.
    /// </summary>
    void ClearViolation(string key);

    /// <summary>
    /// Clear all violations from tracker.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Clear violations older than the retention period.
    /// </summary>
    void ClearOldViolations(TimeSpan retentionPeriod);
}

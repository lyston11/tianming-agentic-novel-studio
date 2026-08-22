using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public interface IWorkspaceViolationTracker
{
    void TrackViolation(WorkspaceViolation violation);

    WorkspaceViolation RecordViolation(
        string userId,
        string projectId,
        string requestId,
        string path,
        ViolationType type,
        string message);

    IEnumerable<WorkspaceViolation> GetViolations(
        ViolationSeverity? severity = null,
        DateTime? since = null,
        string? userId = null,
        string? projectId = null);

    void ClearOldViolations(TimeSpan retentionPeriod);

    void ClearViolation(string key);
}

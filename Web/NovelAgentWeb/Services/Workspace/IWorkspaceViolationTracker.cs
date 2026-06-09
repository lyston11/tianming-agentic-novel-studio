using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public interface IWorkspaceViolationTracker
{
    void TrackViolation(WorkspaceViolation violation);

    IEnumerable<WorkspaceViolation> GetViolations(
        ViolationSeverity? severity = null,
        DateTime? since = null,
        string? userId = null,
        string? projectId = null);

    void ClearOldViolations(TimeSpan retentionPeriod);
}

using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IProductionWorkflowBridge
{
    Task<IReadOnlyList<WorkflowProductionEventSummary>> LoadProjectEventsAsync(
        string projectId,
        int limit = 200,
        CancellationToken cancellationToken = default);
}

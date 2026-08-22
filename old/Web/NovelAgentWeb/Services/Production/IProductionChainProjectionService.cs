using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IProductionChainProjectionService
{
    IReadOnlyList<WorkflowProductionChain> BuildWorkflowChains(
        IEnumerable<WorkflowProductionEventSummary> productionEvents);

    List<NovelProductionChainState> BuildNovelChains(
        IReadOnlyList<NovelProductionEventState> events,
        IReadOnlyList<NovelProductionPackageState> packages,
        IReadOnlyList<NovelProductionRevisionPlanState> revisionPlans,
        IReadOnlyList<NovelProductionOutboxState> outboxEvents,
        IReadOnlyList<NovelProductionRebuildLinkState> rebuildLinks);
}

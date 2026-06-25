namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IProductionOutboxDispatcher
{
    Task<int> DispatchPendingAsync(int maxItems = 20, CancellationToken cancellationToken = default);
}

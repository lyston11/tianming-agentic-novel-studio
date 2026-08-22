namespace TM.Web.NovelAgentWeb.Services.VectorStore;

public interface IQdrantHealthProbe
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

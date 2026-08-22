namespace TM.Web.NovelAgentWeb.Services.Vectorization;

public interface IOutboxMaterialVectorIndexingService
{
    Task IndexMaterialAsync(string materialId, string userId, CancellationToken ct = default);
}

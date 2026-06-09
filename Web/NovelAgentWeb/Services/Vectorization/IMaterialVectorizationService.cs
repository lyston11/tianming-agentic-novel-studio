namespace TM.Web.NovelAgentWeb.Services.Vectorization;

public interface IMaterialVectorizationService
{
    Task VectorizeMaterialAsync(string materialId, string userId, CancellationToken ct = default);
    Task<int> VectorizeAllMaterialsAsync(string projectId, string userId, CancellationToken ct = default);
}

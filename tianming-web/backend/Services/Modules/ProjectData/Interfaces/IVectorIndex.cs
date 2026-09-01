using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public record VectorSearchHit(string Key, float Score);

    public interface IVectorIndex
    {
        int Count { get; }

        Task<bool> UpsertAsync(string key, float[] vector, CancellationToken ct = default);

        Task<bool> UpsertBatchAsync(IReadOnlyList<(string Key, float[] Vector)> items, CancellationToken ct = default);

        Task<bool> RemoveAsync(string key, CancellationToken ct = default);

        Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, int topK, CancellationToken ct = default);

        Task<bool> ExistsAsync(string key, CancellationToken ct = default);

        IReadOnlyCollection<string> GetAllKeys();

        Task LoadAsync(CancellationToken ct = default);

        Task SaveAsync(CancellationToken ct = default);

        void InvalidateCache();
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public record FirstChapterEntry(string EntityId, string ChapterId, int ChunkPosition, DateTime CapturedAt);

    public interface IEntityFirstChapterIndex
    {
        int Count { get; }

        Task<bool> TryCaptureAsync(string entityId, string entityName, string entityDescription, string chapterId, CancellationToken ct = default);

        Task<bool> RebuildAsync(string entityId, string entityName, string entityDescription, CancellationToken ct = default);

        Task<int> InvalidateByChapterAsync(string chapterId, CancellationToken ct = default);

        Task<int> InvalidateByEntitiesAsync(System.Collections.Generic.IEnumerable<string> entityIds, CancellationToken ct = default);

        Task<FirstChapterEntry?> GetAsync(string entityId);

        bool Contains(string entityId);

        System.Collections.Generic.IReadOnlyList<FirstChapterEntry> GetAll();

        Task LoadAsync(CancellationToken ct = default);

        Task SaveAsync(CancellationToken ct = default);

        void InvalidateCache();
    }
}

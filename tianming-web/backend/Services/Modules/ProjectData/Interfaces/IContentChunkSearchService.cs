using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public sealed record ContentChunkHit(string ChapterId, int Position, string Content, double Score);

    public interface IContentChunkSearchService
    {
        Task<List<ContentChunkHit>> SearchAsync(string query, int topK = 5);

        Task<List<ContentChunkHit>> SearchByChapterAsync(string chapterId, int topK = 2);

        Task InvalidateChapterAsync(string chapterId);

        Task<List<ContentChunkHit>> SearchByChapterPositionAsync(
            string chapterId,
            int startPosition,
            int windowSize = 1,
            CancellationToken ct = default);

        Task<IReadOnlyList<ContentChunkHit>> GetChunksAsync(
            string chapterId,
            CancellationToken ct = default);

        void InvalidateCache();
    }
}

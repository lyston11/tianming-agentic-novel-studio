using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IChapterCatalogService
    {
        Task<IReadOnlyList<string>> ListChapterIdsAsync(CancellationToken ct = default);

        Task<string> GetLatestChapterIdAsync(CancellationToken ct = default);

        Task<int> GetLastChapterNumberOfVolumeAsync(int volumeNumber, CancellationToken ct = default);

        Task<string> GetChapterContentAsync(string chapterId, CancellationToken ct = default);

        Task<bool> ChapterExistsAsync(string chapterId, CancellationToken ct = default);
    }
}

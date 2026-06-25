using System.Collections.Generic;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IChapterSummaryService
    {
        Task SetSummaryAsync(
            string chapterId,
            string summary,
            string? runtimeRunId = null,
            string? packageId = null);

        Task<string> GetSummaryAsync(string chapterId);

        Task<Dictionary<string, string>> GetPreviousSummariesAsync(string currentChapterId, int count);

        Task<Dictionary<string, string>> GetVolumeSummariesAsync(int volumeNumber);

        Task<Dictionary<string, string>> GetAllSummariesAsync();

        void InvalidateCache();
    }
}

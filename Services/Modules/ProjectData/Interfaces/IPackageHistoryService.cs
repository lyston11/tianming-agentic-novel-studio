using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Publishing;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IPackageHistoryService
    {
        int RetainCount { get; set; }

        Task<bool> SaveCurrentToHistoryAsync();

        Task<List<PackageHistoryEntry>> GetAllHistoryAsync();

        Task<bool> RestoreVersionAsync(int version);

        void CleanupOldHistory();

        System.Threading.Tasks.Task<PackageVersionDiff> GetVersionDiffAsync(int historyVersion);

        Task<bool> ClearAllAsync();
    }
}

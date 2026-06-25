using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IVolumeFactArchiveService
    {
        Task<List<VolumeFactArchive>> GetPreviousArchivesAsync(int currentVolumeNumber);

        void InvalidateCache();
    }
}

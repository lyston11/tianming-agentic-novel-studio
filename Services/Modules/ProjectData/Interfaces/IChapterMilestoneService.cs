using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IChapterMilestoneService
    {
        Task<List<VolumeMilestoneEntry>> GetPreviousMilestonesAsync(int currentVolumeNumber);

        void InvalidateCache();
    }
}

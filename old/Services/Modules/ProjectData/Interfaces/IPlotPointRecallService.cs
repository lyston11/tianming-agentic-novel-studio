using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IPlotPointRecallService
    {
        Task<List<PlotPointEntry>> SearchRecentAsync(
            string currentChapterId,
            HashSet<string>? characterIds,
            HashSet<string>? otherEntityIds,
            int lookbackVolumes = 0);

        void InvalidateCache();
    }
}

using System;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class FactSnapshotExtractor
    {
        #region 私有方法 - 优先级计算

        private static int ImportanceScore(PlotPointEntry p)
        {
            var score = p.Importance switch
            {
                "critical" => 3,
                "important" => 2,
                _ => 1
            };
            if (string.Equals(p.Storyline, "main", StringComparison.OrdinalIgnoreCase))
                score += 2;
            return score;
        }

        #endregion
    }
}

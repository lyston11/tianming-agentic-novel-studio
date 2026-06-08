using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class FactSnapshotExtractor
    {
        #region 私有方法 - 冲突进度抽取

        private async Task<List<ConflictProgressSnapshot>> ExtractConflictProgressAsync(
            List<string> conflictIds)
        {
            var result = new List<ConflictProgressSnapshot>();

            if (conflictIds == null || conflictIds.Count == 0)
                return result;

            try
            {
                var guide = await AggregateConflictProgressGuideAsync().ConfigureAwait(false);

                foreach (var conflictId in conflictIds)
                {
                    if (!guide.Conflicts.TryGetValue(conflictId, out var conflictEntry))
                        continue;

                    var recentProgress = (conflictEntry.ProgressPoints ?? new List<ConflictProgressPoint>())
                        .Where(p => !string.IsNullOrWhiteSpace(p.Event))
                        .TakeLast(10)
                        .Select(p => $"{p.Chapter}: {p.Event}")
                        .ToList();

                    result.Add(new ConflictProgressSnapshot
                    {
                        Id = conflictId,
                        Name = conflictEntry.Name,
                        Status = conflictEntry.Status,
                        RecentProgress = recentProgress
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取冲突进度失败: {ex.Message}");
            }

            return result;
        }

        #endregion
    }
}

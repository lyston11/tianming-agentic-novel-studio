using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class FactSnapshotExtractor
    {
        #region 私有方法 - 关键情节抽取

        private async Task<List<PlotPointSnapshot>> ExtractPlotPointsAsync(
            string currentChapterId,
            List<string>? characterIds,
            List<string> otherEntityIds)
        {
            var result = new List<PlotPointSnapshot>();

            if ((characterIds == null || characterIds.Count == 0) &&
                (otherEntityIds == null || otherEntityIds.Count == 0))
                return result;

            try
            {
                var charSet = new HashSet<string>(characterIds ?? new List<string>());
                var otherSet = new HashSet<string>(otherEntityIds ?? new List<string>());

                var candidates = await ServiceLocator.Get<PlotPointsIndexService>().SearchRecentAsync(
                    currentChapterId, charSet, otherSet, lookbackVolumes: 0).ConfigureAwait(false);

                if (candidates.Count == 0)
                    return result;

                var chapterComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                var relatedPlotPoints = candidates
                    .OrderByDescending(p => ImportanceScore(p))
                    .ThenByDescending(p => p.Chapter, chapterComparer)
                    .Take(15)
                    .ToList();

                foreach (var plotPoint in relatedPlotPoints)
                {
                    result.Add(new PlotPointSnapshot
                    {
                        Id = plotPoint.Id,
                        Summary = plotPoint.Context,
                        ChapterId = plotPoint.Chapter,
                        RelatedEntityIds = plotPoint.InvolvedCharacters ?? new List<string>(),
                        Storyline = plotPoint.Storyline
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取关键情节失败: {ex.Message}");
            }

            return result;
        }

        #endregion
    }
}

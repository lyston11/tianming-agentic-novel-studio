using System;
using System.Collections.Generic;
using System.Linq;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class UnifiedValidationService : IUnifiedValidationService
    {
        #region 抽样算法

        internal static int CalculateSampleCount(int totalCount)
        {
            var sample = (int)Math.Ceiling(totalCount / 5.0);
            return Math.Max(3, Math.Min(50, sample));
        }

        internal List<ChapterInfo> SampleChapters(List<ChapterInfo> chapters, int maxCount)
        {
            if (chapters == null || chapters.Count == 0)
                return new List<ChapterInfo>();

            if (chapters.Count <= maxCount)
                return chapters.ToList();

            var sampled = new List<ChapterInfo>();
            var totalCount = chapters.Count;

            var step = (double)(totalCount - 1) / (maxCount - 1);

            for (int i = 0; i < maxCount; i++)
            {
                var index = (int)Math.Round(i * step);
                index = Math.Min(index, totalCount - 1);

                if (!sampled.Contains(chapters[index]))
                {
                    sampled.Add(chapters[index]);
                }
            }

            if (!sampled.Contains(chapters[0]))
            {
                sampled.Insert(0, chapters[0]);
                if (sampled.Count > maxCount)
                    sampled.RemoveAt(sampled.Count - 1);
            }

            if (!sampled.Contains(chapters[totalCount - 1]))
            {
                if (sampled.Count >= maxCount)
                    sampled.RemoveAt(sampled.Count - 1);
                sampled.Add(chapters[totalCount - 1]);
            }

            return sampled.OrderBy(c => c.ChapterNumber).ToList();
        }

        #endregion
    }
}

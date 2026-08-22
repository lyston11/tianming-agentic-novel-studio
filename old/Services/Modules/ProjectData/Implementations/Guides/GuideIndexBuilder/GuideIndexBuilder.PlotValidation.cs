using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Design.Plot;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideIndexBuilder
    {
        #region 伏笔章节校验（1.6）

        public List<PackageWarning> ValidatePlotRulesChapters(
            List<PlotRulesData> plotRules,
            HashSet<string> allChapterIds)
        {
            var warnings = new List<PackageWarning>();

            foreach (var plotRule in plotRules)
            {
                var storyPhase = plotRule.StoryPhase?.Trim() ?? string.Empty;

                if (!string.IsNullOrEmpty(storyPhase))
                {
                    var looksLikeChapterId = ChapterParserHelper.ParseChapterId(storyPhase).HasValue;
                    if (looksLikeChapterId && !allChapterIds.Contains(storyPhase))
                    {
                        warnings.Add(new PackageWarning
                        {
                            Level = "Error",
                            Source = $"剧情规则: {plotRule.Name}",
                            Message = $"所属阶段 {storyPhase} 看起来是章节ID格式但未匹配现有章节"
                        });
                    }
                }
            }

            foreach (var warning in warnings)
            {
                TM.App.Log($"[GuideIndexBuilder] [{warning.Level}] {warning.Source}: {warning.Message}");
            }

            return warnings;
        }

        #endregion
    }
}

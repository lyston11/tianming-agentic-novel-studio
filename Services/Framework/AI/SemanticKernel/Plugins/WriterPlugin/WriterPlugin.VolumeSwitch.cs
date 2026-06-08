using System;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.UI.Workspace.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Implementations;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class WriterPlugin
    {
        #region F5: 卷末自动切换

        private async Task TryAutoSwitchVolumeAfterGenerationAsync(string chapterId)
        {
            try
            {
                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (!parsed.HasValue) return;

                var vol = parsed.Value.volumeNumber;
                var ch = parsed.Value.chapterNumber;

                await VolumeDesignService.InitializeAsync().ConfigureAwait(false);
                var designs = VolumeDesignService.GetAllVolumeDesigns();

                var currentDesign = designs.FirstOrDefault(v => v.VolumeNumber == vol);
                if (currentDesign == null) return;

                var effectiveEndChapter = currentDesign.EndChapter;

                if (effectiveEndChapter <= 0)
                {
                    effectiveEndChapter = await ResolveVolumeEndChapterFromGuideAsync(vol).ConfigureAwait(false);
                    if (effectiveEndChapter > 0)
                        TM.App.Log($"[WriterPlugin] F5: 第{vol}卷EndChapter未配置，从ContentGuide推断为{effectiveEndChapter}");
                }

                if (effectiveEndChapter <= 0 || ch != effectiveEndChapter)
                    return;

                var nextDesign = designs
                    .Where(v => v.VolumeNumber > vol)
                    .OrderBy(v => v.VolumeNumber)
                    .FirstOrDefault();

                if (nextDesign == null)
                {
                    TM.App.Log($"[WriterPlugin] F5: 第{vol}卷已是最后一卷，不切换");
                    return;
                }

                var nextStart = nextDesign.StartChapter > 0 ? nextDesign.StartChapter : 1;
                var nextChapterId = ChapterParserHelper.BuildChapterId(nextDesign.VolumeNumber, nextStart);

                CurrentChapterTracker.SetCurrentChapter(nextChapterId, $"第{nextDesign.VolumeNumber}卷（待生成）");
                GlobalToast.Info("卷切换", $"第{vol}卷已完成，已自动切换到第{nextDesign.VolumeNumber}卷");
                TM.App.Log($"[WriterPlugin] F5: 卷末切换 {chapterId} → {nextChapterId}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] F5: 卷末切换检测失败（非致命）: {ex.Message}");
                GlobalToast.Warning("卷切换检测失败", "请手动确认当前卷是否正确");
            }
        }

        private static Task<int> ResolveVolumeEndChapterFromGuideAsync(int volumeNumber)
            => ServiceLocator.Get<GuideContextService>().GetVolumeMaxChapterAsync(volumeNumber);

        #endregion
    }
}

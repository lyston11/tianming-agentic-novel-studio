using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Design.Plot;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class PublishService : IPublishService
    {
        #region 指导文件生成

        private async Task GenerateGuideFilesAsync(string configBasePath)
        {
            try
            {
                var builder = new GuideIndexBuilder(modulePath => _changeDetectionService.GetStatus(modulePath).IsEnabled);
                var guidesPath = Path.Combine(configBasePath, "guides");
                Directory.CreateDirectory(guidesPath);

                var outlineGuide = await builder.BuildOutlineGuideAsync().ConfigureAwait(false);
                await SaveGuideAsync(guidesPath, "outline_guide.json", outlineGuide).ConfigureAwait(false);

                var planningGuide = await builder.BuildPlanningGuideAsync(outlineGuide).ConfigureAwait(false);
                await SaveGuideAsync(guidesPath, "planning_guide.json", planningGuide).ConfigureAwait(false);

                var blueprintGuide = await builder.BuildBlueprintGuideAsync(planningGuide).ConfigureAwait(false);
                await SaveGuideAsync(guidesPath, "blueprint_guide.json", blueprintGuide).ConfigureAwait(false);

                var contentGuide = await builder.BuildContentGuideAsync(blueprintGuide).ConfigureAwait(false);

                if (contentGuide.Chapters.Count == 0)
                    throw new InvalidOperationException(
                        "章节指导为空（0章）：蓝图数据均未启用。" +
                        "请检查蓝图设计是否已启用，确认后重新打包。");

                var contentShards = new SortedDictionary<int, ContentGuide>();
                foreach (var (chapterId, entry) in contentGuide.Chapters)
                {
                    var vol = ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;
                    if (!contentShards.TryGetValue(vol, out var shard))
                    {
                        shard = new ContentGuide();
                        contentShards[vol] = shard;
                    }
                    shard.Chapters[chapterId] = entry;
                    if (contentGuide.ChapterSummaries.TryGetValue(chapterId, out var sum))
                        shard.ChapterSummaries[chapterId] = sum;
                }
                await Task.WhenAll(contentShards.Select(kv =>
                    SaveGuideAsync(guidesPath, GuideManager.GetVolumeFileName("content_guide.json", kv.Key), kv.Value))).ConfigureAwait(false);

                try
                {
                    var newShardNames = new HashSet<string>(
                        contentShards.Keys.Select(v => GuideManager.GetVolumeFileName("content_guide.json", v)),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (var f in Directory.GetFiles(guidesPath, "content_guide_vol*.json", SearchOption.TopDirectoryOnly))
                    {
                        var fn = Path.GetFileName(f);
                        if (!newShardNames.Contains(fn))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                }
                catch { }

                var legacyCgPath = Path.Combine(guidesPath, "content_guide.json");
                if (File.Exists(legacyCgPath))
                    try { File.Delete(legacyCgPath); } catch { }

                var allChapterIds = new HashSet<string>(contentGuide.Chapters.Keys);
                var plotRules = await LoadPlotRulesAsync().ConfigureAwait(false);
                var plotWarnings = builder.ValidatePlotRulesChapters(plotRules, allChapterIds);
                var plotErrors = plotWarnings
                    .Where(w => string.Equals(w.Level, "Error", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (plotErrors.Count > 0)
                {
                    var summary = string.Join(" | ", plotErrors.Select(w => $"{w.Source}: {w.Message}"));
                    throw new InvalidOperationException($"剧情规则章节归属校验失败：{summary}");
                }

                var guideManager = ServiceLocator.Get<GuideManager>();

                var foreshadowTask = builder.BuildForeshadowingStatusGuideAsync();
                var existingForeshadowTask = guideManager.GetGuideAsync<ForeshadowingStatusGuide>("foreshadowing_status_guide.json");
                await Task.WhenAll(foreshadowTask, existingForeshadowTask).ConfigureAwait(false);
                var foreshadowStatus = await foreshadowTask.ConfigureAwait(false);
                var existingForeshadow = await existingForeshadowTask.ConfigureAwait(false);
                foreach (var (id, entry) in existingForeshadow.Foreshadowings)
                    if (foreshadowStatus.Foreshadowings.TryGetValue(id, out var target))
                    {
                        target.IsSetup = entry.IsSetup;
                        target.IsResolved = entry.IsResolved;
                        target.ActualSetupChapter = entry.ActualSetupChapter;
                        target.ActualPayoffChapter = entry.ActualPayoffChapter;
                        target.IsOverdue = entry.IsOverdue;
                    }
                await SaveGuideAsync(guidesPath, "foreshadowing_status_guide.json", foreshadowStatus).ConfigureAwait(false);
                guideManager.EvictCache("foreshadowing_status_guide.json", foreshadowStatus);

                TM.App.Log("[PublishService] 指导文件生成完成（设计类Guide，追踪Guide由运行期独立管理）");

                try
                {
                    await ServiceLocator.Get<LedgerTrimService>().TrimAllAsync().ConfigureAwait(false);
                }
                catch (Exception trimEx)
                {
                    TM.App.Log($"[PublishService] trim err (non-critical): {trimEx.Message}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 指导文件生成失败: {ex.Message}");
                throw;
            }
        }

        private async Task SaveGuideAsync<T>(string guidesPath, string fileName, T guide)
        {
            var filePath = Path.Combine(guidesPath, fileName);
            var tmpPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(stream, guide, JsonOptions).ConfigureAwait(false);
            }
            File.Move(tmpPath, filePath, overwrite: true);

            TM.App.Log($"[PublishService] 已保存指导文件: {fileName}");
        }

        private async Task<List<PlotRulesData>> LoadPlotRulesAsync()
        {
            var filePath = Path.Combine(StoragePathHelper.GetStorageRoot(), "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");

            if (!File.Exists(filePath)) return new List<PlotRulesData>();

            try
            {
                await using var stream = File.OpenRead(filePath);
                var all = await JsonSerializer.DeserializeAsync<List<PlotRulesData>>(stream, JsonOptions).ConfigureAwait(false) ?? new List<PlotRulesData>();
                return all;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 读取剧情规则文件失败: {ex.Message}");
                return new List<PlotRulesData>();
            }
        }

        #endregion
    }
}

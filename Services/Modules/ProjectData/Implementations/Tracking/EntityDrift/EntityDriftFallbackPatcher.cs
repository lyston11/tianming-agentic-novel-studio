using System;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class EntityDriftFallbackPatcher
    {
        private readonly GuideManager _guideManager;

        public EntityDriftFallbackPatcher(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        public async Task<EntityDriftFallbackResult> DetectAndApplyAsync(
            string chapterId,
            string summary,
            ChapterChanges changes,
            int recentVolumes = 5)
        {
            var result = new EntityDriftFallbackResult();
            if (string.IsNullOrWhiteSpace(summary) || changes == null)
                return result;

            var parsed = ChapterParserHelper.ParseChapterIdOrDefault(chapterId);
            var currentVol = parsed.volumeNumber;
            if (currentVol <= 0)
            {
                TM.App.Log($"[EntityDriftFallbackPatcher] 无法解析 chapterId={chapterId}，跳过兜底补录");
                return result;
            }

            var cfg = LayeredContextConfig.TakeSnapshot();
            var minNameLength = cfg.DriftMinNameLength;
            var maxWarn = cfg.DriftWarningsMaxPerEntity;

            foreach (var descriptor in EntityDimensionRegistry.All)
            {
                try
                {
                    var declaredIds = descriptor.ExtractDeclaredIds(changes);
                    var entities = await descriptor.LoadRecentEntitiesAsync(_guideManager, recentVolumes).ConfigureAwait(false);
                    if (entities.Count == 0) continue;

                    foreach (var entity in entities)
                    {
                        if (declaredIds.Contains(entity.Id)) continue;
                        if (string.IsNullOrWhiteSpace(entity.Name) || entity.Name.Length < minNameLength) continue;
                        if (!summary.Contains(entity.Name, StringComparison.OrdinalIgnoreCase)) continue;

                        var warnMsg = $"{chapterId}: 出现于摘要但CHANGES未申报，状态可能不一致";

                        var targetVol = descriptor.IsVolumeScoped ? currentVol : 0;
                        string? dirtyFile = null;
                        try
                        {
                            dirtyFile = await descriptor.AppendDriftWarningAsync(
                                _guideManager, targetVol, entity.Id, entity.Name, warnMsg, maxWarn).ConfigureAwait(false);
                        }
                        catch (Exception warnEx)
                        {
                            TM.App.Log($"[EntityDriftFallbackPatcher] {descriptor.DimensionName} {entity.Name} 写入 DriftWarning 失败（非致命）: {warnEx.Message}");
                        }

                        if (!string.IsNullOrEmpty(dirtyFile))
                            result.DirtyGuideFiles.Add(dirtyFile);

                        if (descriptor.Strategy == DriftStrategy.AutoPatch && descriptor.AutoPatchAction != null)
                        {
                            try
                            {
                                descriptor.AutoPatchAction(changes, entity.Id, entity.Name, "出现于本章正文，CHANGES未申报");
                                declaredIds.Add(entity.Id);
                                result.AutoPatchedCount++;
                                result.Details.Add($"[{descriptor.DimensionName}-补录] {entity.Name}（{entity.Id}）");
                                TM.App.Log($"[EntityDriftFallbackPatcher] 已自动补录 {descriptor.DimensionName} \"{entity.Name}\" 到 CHANGES.{descriptor.ChangeFieldName}: {chapterId}");
                            }
                            catch (Exception patchEx)
                            {
                                TM.App.Log($"[EntityDriftFallbackPatcher] {descriptor.DimensionName} {entity.Name} AutoPatch 失败（非致命）: {patchEx.Message}");
                            }
                        }
                        else
                        {
                            result.WarnOnlyCount++;
                            result.Details.Add($"[{descriptor.DimensionName}-告警] {entity.Name}（{entity.Id}）");
                            TM.App.Log($"[EntityDriftFallbackPatcher] {descriptor.DimensionName} \"{entity.Name}\" 漂移告警（仅 DriftWarnings，不补 CHANGES）: {chapterId}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[EntityDriftFallbackPatcher] {descriptor.DimensionName}维度扫描失败（非致命）: {ex.Message}");
                }
            }

            if (result.HasAnyDrift)
                TM.App.Log($"[EntityDriftFallbackPatcher] {chapterId} 兜底完成：补录 {result.AutoPatchedCount} 条 + 告警 {result.WarnOnlyCount} 条，DirtyFiles={result.DirtyGuideFiles.Count}");

            return result;
        }
    }
}

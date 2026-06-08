using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideContextService
    {
        #region Extractors

        public async Task<List<Models.Design.Characters.CharacterRulesData>> ExtractCharactersAsync(List<string>? ids)
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            if (ids == null || ids.Count == 0)
                return new List<Models.Design.Characters.CharacterRulesData>();
            var result = new List<Models.Design.Characters.CharacterRulesData>(ids.Count);
            List<string>? missing = null;
            foreach (var id in ids)
            {
                if (_characterCache.TryGetValue(id, out var val))
                    result.Add(val);
                else
                    (missing ??= new()).Add(id);
            }
            if (missing != null)
                TM.App.Log($"[GuideContextService] 角色ID未找到: {string.Join(", ", missing)}");
            return result;
        }

        public async Task<List<Models.Design.Characters.CharacterRulesData>> GetAllCharactersAsync()
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            return _characterCache.Values.ToList();
        }

        public async Task<List<Models.Design.Location.LocationRulesData>> ExtractLocationsAsync(List<string>? ids)
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            if (ids == null || ids.Count == 0)
                return new List<Models.Design.Location.LocationRulesData>();
            var result = new List<Models.Design.Location.LocationRulesData>(ids.Count);
            List<string>? missing = null;
            foreach (var id in ids)
            {
                if (_locationCache.TryGetValue(id, out var val))
                    result.Add(val);
                else
                    (missing ??= new()).Add(id);
            }
            if (missing != null)
                TM.App.Log($"[GuideContextService] 地点ID未找到: {string.Join(", ", missing)}");
            return result;
        }

        public async Task<List<Models.Design.Location.LocationRulesData>> GetAllLocationsAsync()
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            return _locationCache.Values.ToList();
        }

        public async Task<List<Models.Design.Plot.PlotRulesData>> ExtractPlotRulesAsync(List<string>? ids)
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            if (ids == null || ids.Count == 0)
                return new List<Models.Design.Plot.PlotRulesData>();
            var result = new List<Models.Design.Plot.PlotRulesData>(ids.Count);
            List<string>? missing = null;
            foreach (var id in ids)
            {
                if (_plotRulesCache.TryGetValue(id, out var val))
                    result.Add(val);
                else
                    (missing ??= new()).Add(id);
            }
            if (missing != null)
                TM.App.Log($"[GuideContextService] 剧情规则ID未找到: {string.Join(", ", missing)}");
            return result;
        }

        public async Task<List<Models.Design.Plot.PlotRulesData>> GetAllPlotRulesAsync()
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            return _plotRulesCache.Values.ToList();
        }

        public async Task<List<Models.Design.Factions.FactionRulesData>> ExtractFactionsAsync(List<string>? ids)
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            if (ids == null || ids.Count == 0)
                return new List<Models.Design.Factions.FactionRulesData>();
            var result = new List<Models.Design.Factions.FactionRulesData>(ids.Count);
            List<string>? missing = null;
            foreach (var id in ids)
            {
                if (_factionCache.TryGetValue(id, out var val))
                    result.Add(val);
                else
                    (missing ??= new()).Add(id);
            }
            if (missing != null)
                TM.App.Log($"[GuideContextService] 势力ID未找到: {string.Join(", ", missing)}");
            return result;
        }

        public async Task<List<Models.Design.Factions.FactionRulesData>> GetAllFactionsAsync()
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            return _factionCache.Values.ToList();
        }

        public async Task<List<CreativeMaterialData>> ExtractTemplatesAsync(List<string>? ids)
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            if (ids == null || ids.Count == 0)
                return new List<CreativeMaterialData>();
            var result = new List<CreativeMaterialData>(ids.Count);
            List<string>? missing = null;
            foreach (var id in ids)
            {
                if (_templateCache.TryGetValue(id, out var val))
                    result.Add(val);
                else
                    (missing ??= new()).Add(id);
            }
            if (missing != null)
                TM.App.Log($"[GuideContextService] 创作模板ID未找到: {string.Join(", ", missing)}");
            return result;
        }

        public async Task<List<CreativeMaterialData>> GetAllTemplatesAsync()
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            return _templateCache.Values.ToList();
        }

        public async Task<List<Models.Design.Worldview.WorldRulesData>> ExtractWorldRulesAsync(List<string>? ids)
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            if (ids == null || ids.Count == 0)
                return new List<Models.Design.Worldview.WorldRulesData>();
            var result = new List<Models.Design.Worldview.WorldRulesData>(ids.Count);
            List<string>? missing = null;
            foreach (var id in ids)
            {
                if (_worldRulesCache.TryGetValue(id, out var val))
                    result.Add(val);
                else
                    (missing ??= new()).Add(id);
            }
            if (missing != null)
                TM.App.Log($"[GuideContextService] 世界观规则ID未找到: {string.Join(", ", missing)}");
            return result;
        }

        public async Task<List<Models.Design.Worldview.WorldRulesData>> GetAllWorldRulesAsync()
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            return _worldRulesCache.Values.ToList();
        }

        public async Task<Models.Generate.StrategicOutline.OutlineData> ExtractVolumeAsync(string volumeId)
        {
            await InitializeCacheAsync().ConfigureAwait(false);

            if (_volumeCache.TryGetValue(volumeId, out var volume) && volume != null)
            {
                return volume;
            }

            return new Models.Generate.StrategicOutline.OutlineData();
        }

        public async Task<ChapterData?> ExtractChapterPlanAsync(string chapterPlanId)
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(chapterPlanId))
                return null;
            if (_chapterPlanCache.TryGetValue(chapterPlanId, out var plan))
                return plan;
            TM.App.Log($"[GuideContextService] 章节规划ID未找到: {chapterPlanId}");
            return null;
        }

        public async Task<List<BlueprintData>> ExtractBlueprintsAsync(List<string>? blueprintIds)
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            if (blueprintIds == null || blueprintIds.Count == 0)
                return new List<BlueprintData>();
            var missing = blueprintIds.Where(id => !_blueprintCache.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                TM.App.Log($"[GuideContextService] 章节蓝图ID未找到: {string.Join(", ", missing)}");
            return blueprintIds
                .Select(id => _blueprintCache.TryGetValue(id, out var bp) ? bp : null)
                .Where(bp => bp != null)
                .ToList()!;
        }

        public async Task<VolumeDesignData?> ExtractVolumeDesignAsync(string volumeDesignId)
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(volumeDesignId))
                return null;
            if (_volumeDesignCache.TryGetValue(volumeDesignId, out var volumeDesign))
                return volumeDesign;
            TM.App.Log($"[GuideContextService] 分卷设计ID未找到: {volumeDesignId}");
            return null;
        }

        public async Task<List<Models.Generate.StrategicOutline.OutlineData>> ExtractPreviousOutlinesAsync(List<string> outlineIds)
        {
            await InitializeCacheAsync().ConfigureAwait(false);
            var missing = outlineIds.Where(id => !_volumeCache.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                TM.App.Log($"[GuideContextService] 大纲ID未找到: {string.Join(", ", missing)}");
            return outlineIds
                .Select(id => _volumeCache.TryGetValue(id, out var vol) ? vol : null)
                .Where(vol => vol != null)
                .ToList()!;
        }

        public async Task<ContextIdValidationResult> ValidateContextIdsAsync(ContextIdCollection? contextIds)
        {
            if (contextIds == null)
                return ContextIdValidationResult.Success();

            await InitializeCacheAsync().ConfigureAwait(false);
            var missingIds = new Dictionary<string, List<string>>();

            if (contextIds.Characters?.Count > 0)
            {
                var missing = contextIds.Characters.Where(id => !_characterCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["Characters"] = missing;
            }

            if (contextIds.Locations?.Count > 0)
            {
                var missing = contextIds.Locations.Where(id => !_locationCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["Locations"] = missing;
            }

            if (contextIds.Factions?.Count > 0)
            {
                var missing = contextIds.Factions.Where(id => !_factionCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["Factions"] = missing;
            }

            if (contextIds.Conflicts?.Count > 0)
            {
                var missing = contextIds.Conflicts.Where(id => !_plotRulesCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["Conflicts"] = missing;
            }

            if (contextIds.PlotRules?.Count > 0)
            {
                var missing = contextIds.PlotRules.Where(id => !_plotRulesCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["PlotRules"] = missing;
            }

            if (contextIds.TemplateIds == null || contextIds.TemplateIds.Count == 0)
            {
                TM.App.Log("[GuideContextService] Preflight提示：TemplateIds为空，将跳过创作模板注入（不阻断生成）");
            }
            else
            {
                var missing = contextIds.TemplateIds.Where(id => !_templateCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["TemplateIds"] = missing;
            }

            if (contextIds.WorldRuleIds == null || contextIds.WorldRuleIds.Count == 0)
            {
                TM.App.Log("[GuideContextService] Preflight提示：WorldRuleIds为空，将跳过世界观规则注入（不阻断生成）");
            }
            else
            {
                var missing = contextIds.WorldRuleIds.Where(id => !_worldRulesCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["WorldRuleIds"] = missing;
            }

            if (string.IsNullOrWhiteSpace(contextIds.ChapterPlanId))
            {
                TM.App.Log("[GuideContextService] Preflight提示：ChapterPlanId为空，将跳过章节规划注入（不阻断生成）");
            }
            else if (!_chapterPlanCache.ContainsKey(contextIds.ChapterPlanId))
            {
                missingIds["ChapterPlanId"] = new List<string> { contextIds.ChapterPlanId };
            }

            if (contextIds.BlueprintIds == null || contextIds.BlueprintIds.Count == 0)
            {
                missingIds["BlueprintIds"] = new List<string> { "空列表" };
            }
            else
            {
                var missing = contextIds.BlueprintIds.Where(id => !_blueprintCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["BlueprintIds"] = missing;
            }

            if (string.IsNullOrWhiteSpace(contextIds.VolumeDesignId))
            {
                var inferred = TryInferVolumeDesignIdFromContext(contextIds);
                if (!string.IsNullOrWhiteSpace(inferred))
                {
                    contextIds.VolumeDesignId = inferred;
                    TM.App.Log($"[GuideContextService] Preflight已自动补全VolumeDesignId: {inferred}（ChapterBlueprint={contextIds.ChapterBlueprint}）");
                }
                else
                {
                    missingIds["VolumeDesignId"] = new List<string> { "空值" };
                }
            }
            else if (!_volumeDesignCache.ContainsKey(contextIds.VolumeDesignId))
            {
                missingIds["VolumeDesignId"] = new List<string> { contextIds.VolumeDesignId };
            }

            if (contextIds.ForeshadowingSetups?.Count > 0 || contextIds.ForeshadowingPayoffs?.Count > 0)
            {
                try
                {
                    var fowGuide = await ServiceLocator.Get<GuideManager>().GetGuideAsync<ForeshadowingStatusGuide>("foreshadowing_status_guide.json").ConfigureAwait(false);
                    if (contextIds.ForeshadowingSetups?.Count > 0)
                    {
                        var missing = contextIds.ForeshadowingSetups.Where(id => !fowGuide.Foreshadowings.ContainsKey(id)).ToList();
                        if (missing.Count > 0)
                            missingIds["ForeshadowingSetups"] = missing;
                    }
                    if (contextIds.ForeshadowingPayoffs?.Count > 0)
                    {
                        var missing = contextIds.ForeshadowingPayoffs.Where(id => !fowGuide.Foreshadowings.ContainsKey(id)).ToList();
                        if (missing.Count > 0)
                            missingIds["ForeshadowingPayoffs"] = missing;
                    }
                }
                catch (Exception ex) { TM.App.Log($"[GuideContextService] 伏笔ID校验失败: {ex.Message}"); }
            }

            if (!string.IsNullOrEmpty(contextIds.VolumeOutline))
            {
                if (!_volumeCache.ContainsKey(contextIds.VolumeOutline))
                    missingIds["VolumeOutline"] = new List<string> { contextIds.VolumeOutline };
            }

            if (!string.IsNullOrEmpty(contextIds.ChapterBlueprint))
            {
                var blueprintGuide = await LoadGuideAsync<Models.Guides.BlueprintGuide>("blueprint_guide.json").ConfigureAwait(false);
                var found = blueprintGuide?.Chapters?.ContainsKey(contextIds.ChapterBlueprint) == true;
                if (!found)
                    missingIds["ChapterBlueprint"] = new List<string> { contextIds.ChapterBlueprint };
            }

            if (!string.IsNullOrEmpty(contextIds.PreviousChapter))
            {
                var contentGuide = await GetContentGuideAsync().ConfigureAwait(false);
                var found = contentGuide?.Chapters?.ContainsKey(contextIds.PreviousChapter) == true;
                if (!found)
                    missingIds["PreviousChapter"] = new List<string> { contextIds.PreviousChapter };
            }

            if (contextIds.PreviousVolumes?.Count > 0)
            {
                var missing = contextIds.PreviousVolumes.Where(id => !_volumeCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["PreviousVolumes"] = missing;
            }

            if (contextIds.PreviousOutlines?.Count > 0)
            {
                var missing = contextIds.PreviousOutlines.Where(id => !_volumeCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["PreviousOutlines"] = missing;
            }

            if (missingIds.Count > 0)
            {
                var errors = missingIds.Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}").ToList();
                TM.App.Log($"[GuideContextService] Preflight ContextIds 验证失败: {string.Join("; ", errors)}");
                return ContextIdValidationResult.Failed(missingIds);
            }

            return ContextIdValidationResult.Success();
        }

        private string? TryInferVolumeDesignIdFromContext(ContextIdCollection contextIds)
        {
            try
            {
                if (contextIds == null)
                {
                    return null;
                }

                var chapterId = contextIds.ChapterBlueprint;
                if (string.IsNullOrWhiteSpace(chapterId))
                {
                    chapterId = contextIds.PreviousChapter;
                }

                if (string.IsNullOrWhiteSpace(chapterId))
                {
                    return null;
                }

                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (!parsed.HasValue || parsed.Value.volumeNumber <= 0)
                {
                    return null;
                }

                var volumeNumber = parsed.Value.volumeNumber;
                var candidates = _volumeDesignCache.Values
                    .Where(v => v != null
                                && !string.IsNullOrWhiteSpace(v.Id)
                                && v.VolumeNumber == volumeNumber)
                    .OrderByDescending(v => v.UpdatedAt)
                    .ToList();

                if (candidates.Count == 0)
                {
                    return null;
                }

                if (candidates.Count > 1)
                {
                    TM.App.Log($"[GuideContextService] Preflight检测到多个VolumeDesign候选，已取最新: vol={volumeNumber}, count={candidates.Count}");
                }

                return candidates[0].Id;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] Preflight推断VolumeDesignId失败: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}

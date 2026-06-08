using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContextService : IContextService
    {
        #region FullContext

        private enum MaterialScope { Worldview, Character, Faction, Location, Plot }

        private const int ContextCharBudget = 33_000;

        public async Task<string> GetCreativeMaterialsContextAsync()
        {
            if (TM.App.IsDebugMode)
                TM.App.Log("[ContextService] 构建CreativeMaterialsContext");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<creative_materials_catalog>");

            try
            {
                var items = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials").ConfigureAwait(false);

                foreach (var item in items.Where(i => i.IsEnabled))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    sb.AppendLine($"分类：{item.Category}");
                    if (!string.IsNullOrWhiteSpace(item.Genre))
                        sb.AppendLine($"题材类型：{item.Genre}");
                    if (!string.IsNullOrWhiteSpace(item.OverallIdea))
                        sb.AppendLine($"整体构思：{item.OverallIdea}");
                    if (!string.IsNullOrWhiteSpace(item.WorldBuildingMethod))
                        sb.AppendLine($"世界观素材-构建手法：{item.WorldBuildingMethod}");
                    if (!string.IsNullOrWhiteSpace(item.PowerSystemDesign))
                        sb.AppendLine($"世界观素材-力量体系：{item.PowerSystemDesign}");
                    if (!string.IsNullOrWhiteSpace(item.EnvironmentDescription))
                        sb.AppendLine($"世界观素材-环境描写：{item.EnvironmentDescription}");
                    if (!string.IsNullOrWhiteSpace(item.FactionDesign))
                        sb.AppendLine($"世界观素材-势力设计：{item.FactionDesign}");
                    if (!string.IsNullOrWhiteSpace(item.WorldviewHighlights))
                        sb.AppendLine($"世界观素材-亮点：{item.WorldviewHighlights}");
                    if (!string.IsNullOrWhiteSpace(item.ProtagonistDesign))
                        sb.AppendLine($"角色素材-主角塑造：{item.ProtagonistDesign}");
                    if (!string.IsNullOrWhiteSpace(item.SupportingRoles))
                        sb.AppendLine($"角色素材-配角设计：{item.SupportingRoles}");
                    if (!string.IsNullOrWhiteSpace(item.CharacterRelations))
                        sb.AppendLine($"角色素材-人物关系：{item.CharacterRelations}");
                    if (!string.IsNullOrWhiteSpace(item.GoldenFingerDesign))
                        sb.AppendLine($"角色素材-金手指：{item.GoldenFingerDesign}");
                    if (!string.IsNullOrWhiteSpace(item.CharacterHighlights))
                        sb.AppendLine($"角色素材-角色亮点：{item.CharacterHighlights}");
                    if (!string.IsNullOrWhiteSpace(item.PlotStructure))
                        sb.AppendLine($"剧情素材-情节结构：{item.PlotStructure}");
                    if (!string.IsNullOrWhiteSpace(item.ConflictDesign))
                        sb.AppendLine($"剧情素材-冲突设计：{item.ConflictDesign}");
                    if (!string.IsNullOrWhiteSpace(item.ClimaxArrangement))
                        sb.AppendLine($"剧情素材-高潮布局：{item.ClimaxArrangement}");
                    if (!string.IsNullOrWhiteSpace(item.ForeshadowingTechnique))
                        sb.AppendLine($"剧情素材-伏笔设计：{item.ForeshadowingTechnique}");
                    if (!string.IsNullOrWhiteSpace(item.PlotHighlights))
                        sb.AppendLine($"剧情素材-剧情亮点：{item.PlotHighlights}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] GetCreativeMaterialsContextAsync失败: {ex.Message}");
            }

            sb.AppendLine("</creative_materials_catalog>");
            return sb.ToString();
        }

        private async Task<string> BuildCreativeMaterialsStringAsync(MaterialScope scope)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<creative_materials_catalog>");
            try
            {
                var items = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials").ConfigureAwait(false);
                foreach (var item in items.Where(i => i.IsEnabled))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    sb.AppendLine($"分类：{item.Category}");
                    if (!string.IsNullOrWhiteSpace(item.Genre))
                        sb.AppendLine($"题材类型：{item.Genre}");
                    if (!string.IsNullOrWhiteSpace(item.OverallIdea))
                        sb.AppendLine($"整体构思：{item.OverallIdea}");

                    bool needWorldBuilding = scope == MaterialScope.Worldview || scope == MaterialScope.Location;
                    bool needPowerSystem = true;
                    bool needEnvironment = scope == MaterialScope.Worldview || scope == MaterialScope.Location;
                    bool needFactionDesign = scope == MaterialScope.Worldview || scope == MaterialScope.Faction;

                    if (needWorldBuilding && !string.IsNullOrWhiteSpace(item.WorldBuildingMethod))
                        sb.AppendLine($"世界观素材-构建手法：{item.WorldBuildingMethod}");
                    if (needPowerSystem && !string.IsNullOrWhiteSpace(item.PowerSystemDesign))
                        sb.AppendLine($"世界观素材-力量体系：{item.PowerSystemDesign}");
                    if (needEnvironment && !string.IsNullOrWhiteSpace(item.EnvironmentDescription))
                        sb.AppendLine($"世界观素材-环境描写：{item.EnvironmentDescription}");
                    if (needFactionDesign && !string.IsNullOrWhiteSpace(item.FactionDesign))
                        sb.AppendLine($"世界观素材-势力设计：{item.FactionDesign}");
                    if (!string.IsNullOrWhiteSpace(item.WorldviewHighlights))
                        sb.AppendLine($"世界观素材-亮点：{item.WorldviewHighlights}");

                    bool needCharMaterials = scope == MaterialScope.Character || scope == MaterialScope.Plot;
                    if (needCharMaterials)
                    {
                        if (!string.IsNullOrWhiteSpace(item.ProtagonistDesign))
                            sb.AppendLine($"角色素材-主角塑造：{item.ProtagonistDesign}");
                        if (!string.IsNullOrWhiteSpace(item.SupportingRoles))
                            sb.AppendLine($"角色素材-配角设计：{item.SupportingRoles}");
                        if (!string.IsNullOrWhiteSpace(item.CharacterRelations))
                            sb.AppendLine($"角色素材-人物关系：{item.CharacterRelations}");
                        if (!string.IsNullOrWhiteSpace(item.GoldenFingerDesign))
                            sb.AppendLine($"角色素材-金手指：{item.GoldenFingerDesign}");
                        if (!string.IsNullOrWhiteSpace(item.CharacterHighlights))
                            sb.AppendLine($"角色素材-角色亮点：{item.CharacterHighlights}");
                    }

                    if (scope == MaterialScope.Plot)
                    {
                        if (!string.IsNullOrWhiteSpace(item.PlotStructure))
                            sb.AppendLine($"剧情素材-情节结构：{item.PlotStructure}");
                        if (!string.IsNullOrWhiteSpace(item.ConflictDesign))
                            sb.AppendLine($"剧情素材-冲突设计：{item.ConflictDesign}");
                        if (!string.IsNullOrWhiteSpace(item.ClimaxArrangement))
                            sb.AppendLine($"剧情素材-高潮布局：{item.ClimaxArrangement}");
                        if (!string.IsNullOrWhiteSpace(item.ForeshadowingTechnique))
                            sb.AppendLine($"剧情素材-伏笔设计：{item.ForeshadowingTechnique}");
                        if (!string.IsNullOrWhiteSpace(item.PlotHighlights))
                            sb.AppendLine($"剧情素材-剧情亮点：{item.PlotHighlights}");
                    }

                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildCreativeMaterialsStringAsync(scope={scope})失败: {ex.Message}"); }
            sb.AppendLine("</creative_materials_catalog>");
            return sb.ToString();
        }

        public async Task<string> GetWorldviewContextStringAsync()
        {
            var materialsTask = BuildCreativeMaterialsStringAsync(MaterialScope.Worldview);
            var worldviewTask = BuildWorldviewStringAsync();
            await Task.WhenAll(materialsTask, worldviewTask).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"worldview_rules\">");
            sb.Append(await materialsTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await worldviewTask.ConfigureAwait(false));
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetCharacterContextStringAsync()
        {
            var materialsTask = BuildCreativeMaterialsStringAsync(MaterialScope.Character);
            var worldviewTask = BuildWorldviewStringAsync();
            await Task.WhenAll(materialsTask, worldviewTask).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"character_rules\">");
            sb.Append(await materialsTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await worldviewTask.ConfigureAwait(false));
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetFactionContextStringAsync()
        {
            var materialsTask = BuildCreativeMaterialsStringAsync(MaterialScope.Faction);
            var worldviewTask = BuildWorldviewStringAsync();
            var charSummaryTask = BuildCharacterSummaryStringAsync();
            var factionMinTask = BuildFactionMinimalStringAsync();
            await Task.WhenAll(materialsTask, worldviewTask, charSummaryTask, factionMinTask).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"faction_rules\">");
            sb.Append(await materialsTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await worldviewTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await charSummaryTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await factionMinTask.ConfigureAwait(false));
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetLocationContextStringAsync()
        {
            var materialsTask = BuildCreativeMaterialsStringAsync(MaterialScope.Location);
            var worldviewTask = BuildWorldviewStringAsync();
            var factionMinTask = BuildFactionMinimalStringAsync();
            await Task.WhenAll(materialsTask, worldviewTask, factionMinTask).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"location_rules\">");
            sb.Append(await materialsTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await worldviewTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await factionMinTask.ConfigureAwait(false));
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetPlotContextStringAsync()
        {
            var materialsTask = BuildCreativeMaterialsStringAsync(MaterialScope.Plot);
            var worldviewTask = BuildWorldviewStringAsync();
            var charTask = BuildCharacterArcStringAsync();
            var factionTask = BuildFactionSummaryStringAsync();
            var locSumTask = BuildLocationSummaryStringAsync();
            await Task.WhenAll(materialsTask, worldviewTask, charTask, factionTask, locSumTask).ConfigureAwait(false);

            var materialsResult = await materialsTask.ConfigureAwait(false);
            var worldviewResult = await worldviewTask.ConfigureAwait(false);
            var charResult = await charTask.ConfigureAwait(false);
            var factionResult = await factionTask.ConfigureAwait(false);
            var locResult = await locSumTask.ConfigureAwait(false);

            var totalChars = materialsResult.Length + worldviewResult.Length + charResult.Length + factionResult.Length + locResult.Length;
            if (totalChars > ContextCharBudget)
            {
                TM.App.Log($"[ContextService] OPT-017 PlotContext 降级: {totalChars} chars > {ContextCharBudget}");
                charResult = await BuildCharacterStructureStringAsync().ConfigureAwait(false);
                factionResult = await BuildFactionMinimalStringAsync().ConfigureAwait(false);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"plot_rules\">");
            sb.Append(materialsResult);
            sb.AppendLine();
            sb.Append(worldviewResult);
            sb.AppendLine();
            sb.Append(charResult);
            sb.AppendLine();
            sb.Append(factionResult);
            sb.AppendLine();
            sb.Append(locResult);
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetOutlineContextStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"outline\">");
            sb.Append(await GetCoreDesignContextAsync().ConfigureAwait(false));
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetOutlineStructureContextAsync()
        {
            var materialsTask = BuildCreativeMaterialsStringAsync(MaterialScope.Plot);
            var worldviewTask = BuildWorldviewStringAsync();
            var charTask = BuildCharacterStructureStringAsync();
            var factionTask = BuildFactionMinimalStringAsync();
            var plotTask = BuildPlotRulesStructureStringAsync(null);
            await Task.WhenAll(materialsTask, worldviewTask, charTask, factionTask, plotTask).ConfigureAwait(false);

            var materialsResult = await materialsTask.ConfigureAwait(false);
            var worldviewResult = await worldviewTask.ConfigureAwait(false);
            var charResult = await charTask.ConfigureAwait(false);
            var factionResult = await factionTask.ConfigureAwait(false);
            var plotResult = await plotTask.ConfigureAwait(false);

            var totalChars = materialsResult.Length + worldviewResult.Length + charResult.Length + factionResult.Length + plotResult.Length;
            if (totalChars > ContextCharBudget)
            {
                TM.App.Log($"[ContextService] OPT-017 OutlineContext 降级: {totalChars} chars > {ContextCharBudget}");
                plotResult = await BuildPlotRulesOutlineMinStringAsync(null).ConfigureAwait(false);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"outline\">");
            sb.Append(materialsResult);
            sb.AppendLine();
            sb.Append(worldviewResult);
            sb.Append(charResult);
            sb.Append(factionResult);
            sb.Append(plotResult);
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetChapterContextStringAsync()
        {
            var coreTask = GetCoreDesignContextAsync();
            var outlineTask = BuildOutlineStringAsync();
            var volumeDesignTask = BuildVolumeDesignStringAsync();
            await Task.WhenAll(coreTask, outlineTask, volumeDesignTask).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"chapter_planning\">");
            sb.Append(await coreTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await outlineTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await volumeDesignTask.ConfigureAwait(false));
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetChapterContextWithVolumeLocatorAsync(string categoryKey)
        {
            var sb = new System.Text.StringBuilder();

            var volume = await GetVolumeDesignByCategoryAsync(categoryKey).ConfigureAwait(false);
            var volumeFilter = volume != null && volume.VolumeNumber > 0 ? $"第{volume.VolumeNumber}卷" : null;

            var isVolumeName = !string.IsNullOrWhiteSpace(categoryKey)
                               && categoryKey.StartsWith('第')
                               && categoryKey.EndsWith('卷');

            var effectiveVolumeFilter = volumeFilter ?? (isVolumeName ? categoryKey : null);

            var coreTask = !string.IsNullOrWhiteSpace(effectiveVolumeFilter)
                ? GetCoreDesignContextForVolumeAsync(effectiveVolumeFilter)
                : GetCoreDesignContextAsync();
            var outlineTask = BuildOutlineStringAsync();
            await Task.WhenAll(coreTask, outlineTask).ConfigureAwait(false);

            sb.Append(await coreTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await outlineTask.ConfigureAwait(false));

            if (volume != null)
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"<context_block type=\"volume_locator\" title=\"{volume.VolumeTitle}\">");
                sb.AppendLine($"- 卷序号：第{volume.VolumeNumber}卷");
                if (volume.StartChapter > 0 && volume.EndChapter > 0)
                    sb.AppendLine($"- 章节范围：第{volume.StartChapter}-{volume.EndChapter}章");
                if (volume.TargetChapterCount > 0)
                    sb.AppendLine($"- 目标章节数：{volume.TargetChapterCount}");
                if (!string.IsNullOrWhiteSpace(volume.VolumeTheme))
                    sb.AppendLine($"- 卷主题：{volume.VolumeTheme}");
                if (!string.IsNullOrWhiteSpace(volume.StageGoal))
                    sb.AppendLine($"- 阶段目标：{volume.StageGoal}");
                if (!string.IsNullOrWhiteSpace(volume.MainConflict))
                    sb.AppendLine($"- 卷主冲突：{volume.MainConflict}");
                if (!string.IsNullOrWhiteSpace(volume.PressureSource))
                    sb.AppendLine($"- 压力来源：{volume.PressureSource}");
                if (!string.IsNullOrWhiteSpace(volume.KeyEvents))
                    sb.AppendLine($"- 关键转折：{volume.KeyEvents}");
                if (!string.IsNullOrWhiteSpace(volume.OpeningState))
                    sb.AppendLine($"- 卷开篇状态：{volume.OpeningState}");
                if (!string.IsNullOrWhiteSpace(volume.EndingState))
                    sb.AppendLine($"- 卷收束状态：{volume.EndingState}");
                if (!string.IsNullOrWhiteSpace(volume.ChapterAllocationOverview))
                    sb.AppendLine($"- 章节分配总览：{volume.ChapterAllocationOverview}");
                if (!string.IsNullOrWhiteSpace(volume.PlotAllocation))
                    sb.AppendLine($"- 剧情分配：{volume.PlotAllocation}");
                if (!string.IsNullOrWhiteSpace(volume.ChapterGenerationHints))
                    sb.AppendLine($"- 章节生成提示：{volume.ChapterGenerationHints}");
                sb.AppendLine("</context_block>");
            }
            else
            {
                sb.AppendLine();
                sb.Append(await BuildVolumeDesignStringAsync().ConfigureAwait(false));
            }

            return sb.ToString();
        }

        public async Task<string> GetBlueprintContextWithChapterLocatorAsync(string chapterId)
        {
            var sb = new System.Text.StringBuilder();

            var chapter = await GetChapterDataByChapterIdAsync(chapterId).ConfigureAwait(false);
            string? volumeFilter = null;
            VolumeDesignData? volumeData = null;
            if (chapter != null)
            {
                var vKey = !string.IsNullOrWhiteSpace(chapter.CategoryId) ? chapter.CategoryId : chapter.Category;
                volumeData = await GetVolumeDesignByCategoryAsync(vKey).ConfigureAwait(false);
                if (volumeData != null && volumeData.VolumeNumber > 0)
                    volumeFilter = $"第{volumeData.VolumeNumber}卷";
            }

            var effectiveCharFilter = chapter?.ReferencedCharacterNames?.Count > 0
                ? (IReadOnlyCollection<string>)chapter.ReferencedCharacterNames
                : (volumeData?.ReferencedCharacterNames?.Count > 0 ? volumeData.ReferencedCharacterNames : null);
            var effectiveFacFilter = chapter?.ReferencedFactionNames?.Count > 0
                ? (IReadOnlyCollection<string>)chapter.ReferencedFactionNames
                : (volumeData?.ReferencedFactionNames?.Count > 0 ? volumeData.ReferencedFactionNames : null);
            var effectiveLocFilter = chapter?.ReferencedLocationNames?.Count > 0
                ? (IReadOnlyCollection<string>)chapter.ReferencedLocationNames
                : (volumeData?.ReferencedLocationNames?.Count > 0 ? volumeData.ReferencedLocationNames : null);
            Task<string> coreTask;
            if (effectiveCharFilter != null || effectiveFacFilter != null || effectiveLocFilter != null)
                coreTask = GetCoreDesignContextForEntityFilterAsync(
                    effectiveCharFilter, effectiveFacFilter, effectiveLocFilter,
                    !string.IsNullOrWhiteSpace(volumeFilter) ? volumeFilter : null);
            else if (!string.IsNullOrWhiteSpace(volumeFilter))
                coreTask = GetCoreDesignContextForVolumeAsync(volumeFilter);
            else
                coreTask = GetCoreDesignContextAsync();
            var outlineTask = BuildOutlineStringAsync();
            var volumeDesignTask = volumeData == null ? BuildVolumeDesignStringAsync() : Task.FromResult(string.Empty);
            var planningVolumeKey = chapter != null
                ? (!string.IsNullOrWhiteSpace(chapter.CategoryId) ? chapter.CategoryId : chapter.Category)
                : null;
            var adjacentTask = BuildAdjacentChapterContextAsync(chapter?.ChapterNumber ?? 0, planningVolumeKey);
            await Task.WhenAll(coreTask, outlineTask, volumeDesignTask, adjacentTask).ConfigureAwait(false);

            sb.Append(await coreTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await outlineTask.ConfigureAwait(false));
            sb.AppendLine();

            if (volumeData == null)
            {
                sb.Append(await volumeDesignTask.ConfigureAwait(false));
                sb.AppendLine();
            }

            var adjacentContext = await adjacentTask.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(adjacentContext))
            {
                sb.Append(adjacentContext);
                sb.AppendLine();
            }

            if (chapter != null)
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"<context_block type=\"chapter_locator\" title=\"{chapter.ChapterTitle}\">");
                sb.AppendLine($"- 章节序号：第{chapter.ChapterNumber}章");
                sb.AppendLine($"- 所属卷：{chapter.Category}");
                if (!string.IsNullOrWhiteSpace(chapter.ChapterTheme))
                    sb.AppendLine($"- 章节主题：{chapter.ChapterTheme}");
                if (!string.IsNullOrWhiteSpace(chapter.MainGoal))
                    sb.AppendLine($"- 本章目标：{chapter.MainGoal}");
                if (!string.IsNullOrWhiteSpace(chapter.ResistanceSource))
                    sb.AppendLine($"- 阻力来源：{chapter.ResistanceSource}");
                if (!string.IsNullOrWhiteSpace(chapter.KeyTurn))
                    sb.AppendLine($"- 关键转折：{chapter.KeyTurn}");
                if (!string.IsNullOrWhiteSpace(chapter.Hook))
                    sb.AppendLine($"- 结尾钉子：{chapter.Hook}");
                if (!string.IsNullOrWhiteSpace(chapter.WorldInfoDrop))
                    sb.AppendLine($"- 世界观投放：{chapter.WorldInfoDrop}");
                if (!string.IsNullOrWhiteSpace(chapter.CharacterArcProgress))
                    sb.AppendLine($"- 角色弧光推进：{chapter.CharacterArcProgress}");
                if (!string.IsNullOrWhiteSpace(chapter.ReaderExperienceGoal))
                    sb.AppendLine($"- 读者体验目标：{chapter.ReaderExperienceGoal}");
                if (!string.IsNullOrWhiteSpace(chapter.MainPlotProgress))
                    sb.AppendLine($"- 主线推进点：{chapter.MainPlotProgress}");
                if (!string.IsNullOrWhiteSpace(chapter.Foreshadowing))
                    sb.AppendLine($"- 伏笔埋设/回收：{chapter.Foreshadowing}");
                sb.AppendLine("</context_block>");

                if (volumeData != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"<context_block type=\"parent_volume\" title=\"{volumeData.VolumeTitle}\">");
                    sb.AppendLine($"- 卷序号：第{volumeData.VolumeNumber}卷");
                    if (volumeData.StartChapter > 0 && volumeData.EndChapter > 0)
                        sb.AppendLine($"- 章节范围：第{volumeData.StartChapter}-{volumeData.EndChapter}章");
                    if (volumeData.TargetChapterCount > 0)
                        sb.AppendLine($"- 目标章节数：{volumeData.TargetChapterCount}");
                    if (!string.IsNullOrWhiteSpace(volumeData.VolumeTheme))
                        sb.AppendLine($"- 卷主题：{volumeData.VolumeTheme}");
                    if (!string.IsNullOrWhiteSpace(volumeData.StageGoal))
                        sb.AppendLine($"- 阶段目标：{volumeData.StageGoal}");
                    if (!string.IsNullOrWhiteSpace(volumeData.MainConflict))
                        sb.AppendLine($"- 卷主冲突：{volumeData.MainConflict}");
                    if (!string.IsNullOrWhiteSpace(volumeData.PressureSource))
                        sb.AppendLine($"- 压力来源：{volumeData.PressureSource}");
                    if (!string.IsNullOrWhiteSpace(volumeData.KeyEvents))
                        sb.AppendLine($"- 关键转折：{volumeData.KeyEvents}");
                    if (!string.IsNullOrWhiteSpace(volumeData.OpeningState))
                        sb.AppendLine($"- 卷开篇状态：{volumeData.OpeningState}");
                    if (!string.IsNullOrWhiteSpace(volumeData.EndingState))
                        sb.AppendLine($"- 卷收束状态：{volumeData.EndingState}");
                    if (!string.IsNullOrWhiteSpace(volumeData.ChapterAllocationOverview))
                        sb.AppendLine($"- 章节分配总览：{volumeData.ChapterAllocationOverview}");
                    if (!string.IsNullOrWhiteSpace(volumeData.PlotAllocation))
                        sb.AppendLine($"- 剧情分配：{volumeData.PlotAllocation}");
                    if (!string.IsNullOrWhiteSpace(volumeData.ChapterGenerationHints))
                        sb.AppendLine($"- 章节生成提示：{volumeData.ChapterGenerationHints}");
                    sb.AppendLine("</context_block>");
                }
            }

            return sb.ToString();
        }

        private async Task<VolumeDesignData?> GetVolumeDesignByCategoryAsync(string categoryKey)
        {
            if (string.IsNullOrWhiteSpace(categoryKey)) return null;

            var key = categoryKey.Trim();

            try
            {
                var volumeDesigns = await LoadFunctionDataAsync<VolumeDesignData>("VolumeDesign").ConfigureAwait(false);

                var candidates = volumeDesigns
                    .Where(v => v.IsEnabled)
                    .ToList();

                var exact = candidates.FirstOrDefault(v =>
                    (!string.IsNullOrWhiteSpace(v.CategoryId) && string.Equals(v.CategoryId, key, StringComparison.Ordinal)) ||
                    string.Equals(v.Category, key, StringComparison.Ordinal) ||
                    string.Equals(v.Id, key, StringComparison.Ordinal) ||
                    string.Equals(v.Name, key, StringComparison.Ordinal) ||
                    string.Equals(v.VolumeTitle, key, StringComparison.Ordinal));
                if (exact != null) return exact;

                int volNum = 0;
                var match = VolNumKeyRegex.Match(key);
                if (match.Success)
                {
                    int.TryParse(match.Groups[1].Value, out volNum);
                }
                else
                {
                    var m2 = VolPrefixRegex.Match(key);
                    if (m2.Success)
                        int.TryParse(m2.Groups[1].Value, out volNum);
                    else
                        int.TryParse(new string(key.Where(char.IsDigit).ToArray()), out volNum);
                }
                if (volNum > 0)
                {
                    var byNum = candidates.FirstOrDefault(v => v.VolumeNumber == volNum);
                    if (byNum != null) return byNum;
                }

                var fuzzy = candidates.FirstOrDefault(v =>
                    (!string.IsNullOrWhiteSpace(v.Name) && v.Name.Contains(key, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(v.VolumeTitle) && v.VolumeTitle.Contains(key, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(v.Category) && v.Category.Contains(key, StringComparison.OrdinalIgnoreCase)));
                return fuzzy;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] GetVolumeDesignByCategoryAsync失败: {ex.Message}");
                return null;
            }
        }

        private async Task<ChapterData?> GetChapterDataByChapterIdAsync(string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId)) return null;

            try
            {
                var match = VolChIdRegex.Match(chapterId);
                if (!match.Success) return null;

                var volumeNumber = int.Parse(match.Groups[1].Value);
                var chapterNumber = int.Parse(match.Groups[2].Value);
                var chapters = await LoadFunctionDataAsync<ChapterData>("Chapter").ConfigureAwait(false);

                var volumePrefix = $"第{volumeNumber}卷";
                return chapters.FirstOrDefault(c => c.IsEnabled &&
                    c.ChapterNumber == chapterNumber &&
                    (
                        (!string.IsNullOrEmpty(c.Volume) &&
                            (string.Equals(c.Volume, volumePrefix, StringComparison.Ordinal) ||
                             c.Volume.StartsWith(volumePrefix + " ", StringComparison.Ordinal))) ||
                        (!string.IsNullOrEmpty(c.Category) &&
                            (string.Equals(c.Category, volumePrefix, StringComparison.Ordinal) ||
                             c.Category.StartsWith(volumePrefix + " ", StringComparison.Ordinal))) ||
                        (!string.IsNullOrWhiteSpace(c.CategoryId) &&
                            (string.Equals(c.CategoryId, volumePrefix, StringComparison.Ordinal) ||
                             c.CategoryId.StartsWith(volumePrefix + " ", StringComparison.Ordinal)))
                    ));
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] GetChapterDataByChapterIdAsync失败: {ex.Message}");
                return null;
            }
        }

        public Task<string> GetVolumeDesignListAsync() => BuildVolumeDesignStringAsync();

        private async Task<string> BuildVolumeDesignStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<volume_designs>");
            try
            {
                var volumes = await LoadFunctionDataAsync<Models.Generate.VolumeDesign.VolumeDesignData>("VolumeDesign").ConfigureAwait(false);
                foreach (var item in volumes.Where(i => i.IsEnabled))
                {
                    sb.AppendLine($"<item name=\"第{item.VolumeNumber}卷 {item.VolumeTitle}\">");
                    if (!string.IsNullOrWhiteSpace(item.VolumeTheme))
                        sb.AppendLine($"卷主题：{item.VolumeTheme}");
                    if (!string.IsNullOrWhiteSpace(item.StageGoal))
                        sb.AppendLine($"阶段目标：{item.StageGoal}");
                    if (item.TargetChapterCount > 0)
                        sb.AppendLine($"目标章节数：{item.TargetChapterCount}");
                    if (item.StartChapter > 0 && item.EndChapter > 0)
                        sb.AppendLine($"章节范围：第{item.StartChapter}章-第{item.EndChapter}章");
                    if (!string.IsNullOrWhiteSpace(item.MainConflict))
                        sb.AppendLine($"卷主冲突：{item.MainConflict}");
                    if (!string.IsNullOrWhiteSpace(item.PressureSource))
                        sb.AppendLine($"压力来源：{item.PressureSource}");
                    if (!string.IsNullOrWhiteSpace(item.KeyEvents))
                        sb.AppendLine($"关键转折：{item.KeyEvents}");
                    if (!string.IsNullOrWhiteSpace(item.OpeningState))
                        sb.AppendLine($"卷开篇状态：{item.OpeningState}");
                    if (!string.IsNullOrWhiteSpace(item.EndingState))
                        sb.AppendLine($"卷收束状态：{item.EndingState}");
                    if (!string.IsNullOrWhiteSpace(item.ChapterAllocationOverview))
                        sb.AppendLine($"章节分配总览：{item.ChapterAllocationOverview}");
                    if (!string.IsNullOrWhiteSpace(item.PlotAllocation))
                        sb.AppendLine($"剧情分配：{item.PlotAllocation}");
                    if (!string.IsNullOrWhiteSpace(item.ChapterGenerationHints))
                        sb.AppendLine($"章节生成提示：{item.ChapterGenerationHints}");
                    if (item.ReferencedCharacterNames.Count > 0)
                        sb.AppendLine($"本卷出场角色：{string.Join("、", item.ReferencedCharacterNames)}");
                    if (item.ReferencedFactionNames.Count > 0)
                        sb.AppendLine($"本卷涉及势力：{string.Join("、", item.ReferencedFactionNames)}");
                    if (item.ReferencedLocationNames.Count > 0)
                        sb.AppendLine($"本卷涉及地点：{string.Join("、", item.ReferencedLocationNames)}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildVolumeDesignStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</volume_designs>");
            return sb.ToString();
        }

        private async Task<string> BuildWorldviewStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<worldview_rules>");
            try
            {
                var worldRules = await LoadFunctionDataAsync<WorldRulesData>("WorldRules").ConfigureAwait(false);
                foreach (var item in worldRules.Where(i => i.IsEnabled && HasWorldRulesContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.OneLineSummary))
                        sb.AppendLine($"简介：{item.OneLineSummary}");
                    if (!string.IsNullOrWhiteSpace(item.PowerSystem))
                        sb.AppendLine($"力量体系：{item.PowerSystem}");
                    if (!string.IsNullOrWhiteSpace(item.Cosmology))
                        sb.AppendLine($"宇宙观：{item.Cosmology}");
                    if (!string.IsNullOrWhiteSpace(item.SpecialLaws))
                        sb.AppendLine($"特殊法则：{item.SpecialLaws}");
                    if (!string.IsNullOrWhiteSpace(item.HardRules))
                        sb.AppendLine($"硬规则：{item.HardRules}");
                    if (!string.IsNullOrWhiteSpace(item.SoftRules))
                        sb.AppendLine($"软规则：{item.SoftRules}");
                    if (!string.IsNullOrWhiteSpace(item.AncientEra))
                        sb.AppendLine($"创世/古代纪元：{item.AncientEra}");
                    if (!string.IsNullOrWhiteSpace(item.KeyEvents))
                        sb.AppendLine($"关键历史事件：{item.KeyEvents}");
                    if (!string.IsNullOrWhiteSpace(item.ModernHistory))
                        sb.AppendLine($"近代史：{item.ModernHistory}");
                    if (!string.IsNullOrWhiteSpace(item.StatusQuo))
                        sb.AppendLine($"故事开始前现状：{item.StatusQuo}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildWorldviewStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</worldview_rules>");
            return sb.ToString();
        }

        private static string ResolveId(string? idOrName, Dictionary<string, string> map)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return string.Empty;
            return map.TryGetValue(idOrName, out var n) ? n : idOrName;
        }

        private static string ResolveIds(string? ids, Dictionary<string, string> map)
        {
            if (string.IsNullOrWhiteSpace(ids)) return string.Empty;
            return string.Join("、", ids.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => ResolveId(s, map)).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        #endregion
    }
}


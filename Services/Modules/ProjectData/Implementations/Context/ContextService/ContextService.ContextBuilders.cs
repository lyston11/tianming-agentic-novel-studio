using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContextService
    {
        private async Task<(Dictionary<string, string> charIdToName,
                            Dictionary<string, string> factionIdToName,
                            Dictionary<string, string> locIdToName,
                            List<CharacterRulesData> chars,
                            List<FactionRulesData> factions,
                            List<LocationRulesData> locations)> LoadEntityMapsAsync()
        {
            var charsTask = LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
            var factionsTask = LoadFunctionDataAsync<FactionRulesData>("FactionRules");
            var locsTask = LoadFunctionDataAsync<LocationRulesData>("LocationRules");
            await Task.WhenAll(charsTask, factionsTask, locsTask).ConfigureAwait(false);

            var chars = await charsTask.ConfigureAwait(false);
            var factions = await factionsTask.ConfigureAwait(false);
            var locs = await locsTask.ConfigureAwait(false);
            var charMap = chars.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                                  .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
            var factionMap = factions.Where(f => !string.IsNullOrWhiteSpace(f.Id))
                                     .ToDictionary(f => f.Id, f => f.Name, StringComparer.OrdinalIgnoreCase);
            var locMap = locs.Where(l => !string.IsNullOrWhiteSpace(l.Id))
                                 .ToDictionary(l => l.Id, l => l.Name, StringComparer.OrdinalIgnoreCase);
            return (charMap, factionMap, locMap, chars, factions, locs);
        }

        private async Task<string> BuildCharacterSummaryStringAsync(List<CharacterRulesData>? preloaded = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<character_rules>");
            try
            {
                var characterRules = preloaded ?? await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules").ConfigureAwait(false);
                foreach (var item in characterRules.Where(i => i.IsEnabled && HasCharacterContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.CharacterType)) sb.AppendLine($"角色类型：{item.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(item.Identity)) sb.AppendLine($"身份：{item.Identity}");
                    if (!string.IsNullOrWhiteSpace(item.Want)) sb.AppendLine($"外在目标：{item.Want}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildCharacterSummaryStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</character_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildCharacterArcStringAsync(
            List<CharacterRulesData>? preloaded = null,
            Dictionary<string, string>? charIdToName = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<character_rules>");
            try
            {
                var characterRules = preloaded ?? await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules").ConfigureAwait(false);
                charIdToName ??= characterRules.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                    .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var item in characterRules.Where(i => i.IsEnabled && HasCharacterContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.CharacterType)) sb.AppendLine($"角色类型：{item.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(item.Identity)) sb.AppendLine($"身份：{item.Identity}");
                    if (!string.IsNullOrWhiteSpace(item.Want)) sb.AppendLine($"外在目标：{item.Want}");
                    if (!string.IsNullOrWhiteSpace(item.Need)) sb.AppendLine($"内在需求：{item.Need}");
                    if (!string.IsNullOrWhiteSpace(item.FlawBelief)) sb.AppendLine($"致命缺点：{item.FlawBelief}");
                    if (!string.IsNullOrWhiteSpace(item.GrowthPath)) sb.AppendLine($"成长路径：{item.GrowthPath}");
                    if (!string.IsNullOrWhiteSpace(item.Personality)) sb.AppendLine($"性格：{item.Personality}");
                    if (!string.IsNullOrWhiteSpace(item.TargetCharacterName))
                    {
                        var tn = ResolveId(item.TargetCharacterName, charIdToName);
                        if (!string.IsNullOrWhiteSpace(tn)) sb.AppendLine($"关联角色：{tn}");
                    }
                    if (!string.IsNullOrWhiteSpace(item.RelationshipType)) sb.AppendLine($"关系类型：{item.RelationshipType}");
                    if (!string.IsNullOrWhiteSpace(item.EmotionDynamic)) sb.AppendLine($"情感动态：{item.EmotionDynamic}");
                    if (!string.IsNullOrWhiteSpace(item.Relationships)) sb.AppendLine($"关系：{item.Relationships}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildCharacterArcStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</character_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildFactionSummaryStringAsync(
            List<FactionRulesData>? preloaded = null,
            Dictionary<string, string>? charIdToName = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<faction_rules>");
            try
            {
                var factionRules = preloaded ?? await LoadFunctionDataAsync<FactionRulesData>("FactionRules").ConfigureAwait(false);
                if (charIdToName == null)
                {
                    var charRules = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules").ConfigureAwait(false);
                    charIdToName = charRules.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                        .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
                }
                var factionIdToName = factionRules.Where(f => !string.IsNullOrWhiteSpace(f.Id))
                    .ToDictionary(f => f.Id, f => f.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var item in factionRules.Where(i => i.IsEnabled && HasFactionContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.FactionType)) sb.AppendLine($"类型：{item.FactionType}");
                    if (!string.IsNullOrWhiteSpace(item.Goal)) sb.AppendLine($"理念目标：{item.Goal}");
                    if (!string.IsNullOrWhiteSpace(item.Leader)) sb.AppendLine($"领袖：{ResolveId(item.Leader, charIdToName)}");
                    if (!string.IsNullOrWhiteSpace(item.CoreMembers)) sb.AppendLine($"核心成员：{ResolveIds(item.CoreMembers, charIdToName)}");
                    if (!string.IsNullOrWhiteSpace(item.MemberTraits)) sb.AppendLine($"成员特征：{item.MemberTraits}");
                    if (!string.IsNullOrWhiteSpace(item.StrengthTerritory)) sb.AppendLine($"实力/地盘：{item.StrengthTerritory}");
                    if (!string.IsNullOrWhiteSpace(item.Allies)) sb.AppendLine($"盟友：{ResolveIds(item.Allies, factionIdToName)}");
                    if (!string.IsNullOrWhiteSpace(item.Enemies)) sb.AppendLine($"敌对：{ResolveIds(item.Enemies, factionIdToName)}");
                    if (!string.IsNullOrWhiteSpace(item.NeutralCompetitors)) sb.AppendLine($"中立竞争：{ResolveIds(item.NeutralCompetitors, factionIdToName)}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildFactionSummaryStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</faction_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildFactionMinimalStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<faction_rules>");
            try
            {
                var factionRules = await LoadFunctionDataAsync<FactionRulesData>("FactionRules").ConfigureAwait(false);
                foreach (var item in factionRules.Where(i => i.IsEnabled && HasFactionContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.FactionType)) sb.AppendLine($"类型：{item.FactionType}");
                    if (!string.IsNullOrWhiteSpace(item.Goal)) sb.AppendLine($"理念目标：{item.Goal}");
                    if (!string.IsNullOrWhiteSpace(item.StrengthTerritory)) sb.AppendLine($"实力/地盘：{item.StrengthTerritory}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildFactionMinimalStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</faction_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildLocationSummaryStringAsync(List<LocationRulesData>? preloaded = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<location_rules>");
            try
            {
                var locationRules = preloaded ?? await LoadFunctionDataAsync<LocationRulesData>("LocationRules").ConfigureAwait(false);
                foreach (var item in locationRules.Where(i => i.IsEnabled && HasLocationContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.LocationType)) sb.AppendLine($"类型：{item.LocationType}");
                    if (!string.IsNullOrWhiteSpace(item.Description)) sb.AppendLine($"描述：{item.Description}");
                    if (item.Dangers.Count > 0) sb.AppendLine($"危险/禁忌：{string.Join("、", item.Dangers)}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildLocationSummaryStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</location_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildLocationSummaryStringForVolumeAsync(
            IReadOnlyCollection<string> nameFilter,
            List<LocationRulesData>? preloaded = null)
        {
            if (nameFilter == null || nameFilter.Count == 0)
                return await BuildLocationSummaryStringAsync(preloaded).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<location_rules>");
            try
            {
                var all = preloaded ?? await LoadFunctionDataAsync<LocationRulesData>("LocationRules").ConfigureAwait(false);
                var enabledAll = all.Where(i => i.IsEnabled).ToList();
                var filtered = enabledAll.Where(i => HasLocationContent(i) &&
                    nameFilter.Any(n =>
                        string.Equals(n, i.Name, StringComparison.OrdinalIgnoreCase) ||
                        i.Name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                        n.Contains(i.Name, StringComparison.OrdinalIgnoreCase))).ToList();

                if (filtered.Count == 0)
                    return await BuildLocationSummaryStringAsync().ConfigureAwait(false);

                foreach (var item in filtered)
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.LocationType)) sb.AppendLine($"类型：{item.LocationType}");
                    if (!string.IsNullOrWhiteSpace(item.Description)) sb.AppendLine($"描述：{item.Description}");
                    if (item.Dangers.Count > 0) sb.AppendLine($"危险/禁忌：{string.Join("、", item.Dangers)}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
                if (filtered.Count < enabledAll.Count)
                    sb.AppendLine($"（另有 {enabledAll.Count - filtered.Count} 个未涉及地点已过滤）");
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildLocationSummaryStringForVolumeAsync失败: {ex.Message}"); }
            sb.AppendLine("</location_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildCharacterArcStringForVolumeAsync(
            IReadOnlyCollection<string> nameFilter,
            List<CharacterRulesData>? preloaded = null,
            Dictionary<string, string>? charIdToName = null)
        {
            if (nameFilter == null || nameFilter.Count == 0)
                return await BuildCharacterArcStringAsync(preloaded, charIdToName).ConfigureAwait(false);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<character_rules>");
            try
            {
                var all = preloaded ?? await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules").ConfigureAwait(false);
                charIdToName ??= all.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                    .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
                var enabledAll = all.Where(i => i.IsEnabled).ToList();
                var filtered = enabledAll.Where(i => HasCharacterContent(i) &&
                    nameFilter.Any(n =>
                        string.Equals(n, i.Name, StringComparison.OrdinalIgnoreCase) ||
                        i.Name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                        n.Contains(i.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                if (filtered.Count == 0)
                    return await BuildCharacterArcStringAsync(all, charIdToName).ConfigureAwait(false);
                foreach (var item in filtered)
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.CharacterType)) sb.AppendLine($"角色类型：{item.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(item.Identity)) sb.AppendLine($"身份：{item.Identity}");
                    if (!string.IsNullOrWhiteSpace(item.Want)) sb.AppendLine($"外在目标：{item.Want}");
                    if (!string.IsNullOrWhiteSpace(item.Need)) sb.AppendLine($"内在需求：{item.Need}");
                    if (!string.IsNullOrWhiteSpace(item.FlawBelief)) sb.AppendLine($"致命缺点：{item.FlawBelief}");
                    if (!string.IsNullOrWhiteSpace(item.GrowthPath)) sb.AppendLine($"成长路径：{item.GrowthPath}");
                    if (!string.IsNullOrWhiteSpace(item.Personality)) sb.AppendLine($"性格：{item.Personality}");
                    if (!string.IsNullOrWhiteSpace(item.TargetCharacterName))
                    {
                        var tn = ResolveId(item.TargetCharacterName, charIdToName);
                        if (!string.IsNullOrWhiteSpace(tn)) sb.AppendLine($"关联角色：{tn}");
                    }
                    if (!string.IsNullOrWhiteSpace(item.RelationshipType)) sb.AppendLine($"关系类型：{item.RelationshipType}");
                    if (!string.IsNullOrWhiteSpace(item.EmotionDynamic)) sb.AppendLine($"情感动态：{item.EmotionDynamic}");
                    if (!string.IsNullOrWhiteSpace(item.Relationships)) sb.AppendLine($"关系：{item.Relationships}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
                if (filtered.Count < enabledAll.Count)
                    sb.AppendLine($"（另有 {enabledAll.Count - filtered.Count} 个未出场角色已过滤）");
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildCharacterArcStringForVolumeAsync失败: {ex.Message}"); }
            sb.AppendLine("</character_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildFactionSummaryStringForVolumeAsync(
            IReadOnlyCollection<string> nameFilter,
            List<FactionRulesData>? preloaded = null,
            Dictionary<string, string>? charIdToName = null)
        {
            if (nameFilter == null || nameFilter.Count == 0)
                return await BuildFactionSummaryStringAsync(preloaded, charIdToName).ConfigureAwait(false);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<faction_rules>");
            try
            {
                var all = preloaded ?? await LoadFunctionDataAsync<FactionRulesData>("FactionRules").ConfigureAwait(false);
                if (charIdToName == null)
                {
                    var charRules = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules").ConfigureAwait(false);
                    charIdToName = charRules.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                        .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
                }
                var enabledAll = all.Where(i => i.IsEnabled).ToList();
                var factionIdToName = all.Where(f => !string.IsNullOrWhiteSpace(f.Id))
                    .ToDictionary(f => f.Id, f => f.Name, StringComparer.OrdinalIgnoreCase);
                var filtered = enabledAll.Where(i => HasFactionContent(i) &&
                    nameFilter.Any(n =>
                        string.Equals(n, i.Name, StringComparison.OrdinalIgnoreCase) ||
                        i.Name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                        n.Contains(i.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                if (filtered.Count == 0)
                    return await BuildFactionSummaryStringAsync(all, charIdToName).ConfigureAwait(false);
                foreach (var item in filtered)
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.FactionType)) sb.AppendLine($"类型：{item.FactionType}");
                    if (!string.IsNullOrWhiteSpace(item.Goal)) sb.AppendLine($"理念目标：{item.Goal}");
                    if (!string.IsNullOrWhiteSpace(item.Leader)) sb.AppendLine($"领袖：{ResolveId(item.Leader, charIdToName)}");
                    if (!string.IsNullOrWhiteSpace(item.CoreMembers)) sb.AppendLine($"核心成员：{ResolveIds(item.CoreMembers, charIdToName)}");
                    if (!string.IsNullOrWhiteSpace(item.MemberTraits)) sb.AppendLine($"成员特征：{item.MemberTraits}");
                    if (!string.IsNullOrWhiteSpace(item.StrengthTerritory)) sb.AppendLine($"实力/地盘：{item.StrengthTerritory}");
                    if (!string.IsNullOrWhiteSpace(item.Allies)) sb.AppendLine($"盟友：{ResolveIds(item.Allies, factionIdToName)}");
                    if (!string.IsNullOrWhiteSpace(item.Enemies)) sb.AppendLine($"敌对：{ResolveIds(item.Enemies, factionIdToName)}");
                    if (!string.IsNullOrWhiteSpace(item.NeutralCompetitors)) sb.AppendLine($"中立竞争：{ResolveIds(item.NeutralCompetitors, factionIdToName)}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
                if (filtered.Count < enabledAll.Count)
                    sb.AppendLine($"（另有 {enabledAll.Count - filtered.Count} 个未出场势力已过滤）");
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildFactionSummaryStringForVolumeAsync失败: {ex.Message}"); }
            sb.AppendLine("</faction_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildOutlineStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<strategic_outline>");
            try
            {
                var outlines = await LoadFunctionDataAsync<Models.Generate.StrategicOutline.OutlineData>("Outline").ConfigureAwait(false);
                foreach (var item in outlines.Where(i => i.IsEnabled))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (item.TotalChapterCount > 0)
                        sb.AppendLine($"全书总章节数：{item.TotalChapterCount}");
                    if (!string.IsNullOrWhiteSpace(item.OneLineOutline))
                        sb.AppendLine($"一句话大纲：{item.OneLineOutline}");
                    if (!string.IsNullOrWhiteSpace(item.EmotionalTone))
                        sb.AppendLine($"情感基调：{item.EmotionalTone}");
                    if (!string.IsNullOrWhiteSpace(item.PhilosophicalMotif))
                        sb.AppendLine($"哲学母题：{item.PhilosophicalMotif}");
                    if (!string.IsNullOrWhiteSpace(item.Theme))
                        sb.AppendLine($"主题思想：{item.Theme}");
                    if (!string.IsNullOrWhiteSpace(item.CoreConflict))
                        sb.AppendLine($"核心冲突：{item.CoreConflict}");
                    if (!string.IsNullOrWhiteSpace(item.EndingState))
                        sb.AppendLine($"结局/目标状态：{item.EndingState}");
                    if (!string.IsNullOrWhiteSpace(item.VolumeDivision))
                        sb.AppendLine($"卷/幕划分：{item.VolumeDivision}");
                    if (!string.IsNullOrWhiteSpace(item.OutlineOverview))
                        sb.AppendLine($"大纲总览：{item.OutlineOverview}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildOutlineStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</strategic_outline>");
            return sb.ToString();
        }

        private async Task<string> BuildAdjacentChapterContextAsync(int currentChapterNumber, string? volumeCategory)
        {
            if (currentChapterNumber <= 0 || string.IsNullOrWhiteSpace(volumeCategory))
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            try
            {
                var allChapters = await LoadDataListAsync<Models.Generate.ChapterPlanning.ChapterData>(
                    "Modules/Generate/Elements/Chapter", "chapter_data.json").ConfigureAwait(false);

                var key = volumeCategory.Trim();
                var keyVolNum = ExtractVolumeNumberFromText(key);
                var volumeChapters = allChapters
                    .Where(c =>
                        string.Equals(c.Category, key, StringComparison.Ordinal) ||
                        (!string.IsNullOrWhiteSpace(c.CategoryId) && string.Equals(c.CategoryId, key, StringComparison.Ordinal)) ||
                        (!string.IsNullOrWhiteSpace(c.Volume) && (
                            string.Equals(c.Volume, key, StringComparison.Ordinal) ||
                            c.Volume.StartsWith(key + " ", StringComparison.Ordinal) ||
                            (keyVolNum > 0 && ExtractVolumeNumberFromText(c.Volume) == keyVolNum))))
                    .OrderBy(c => c.ChapterNumber)
                    .ToList();

                bool hasAny = false;
                var injectedCount = 0;

                var n3 = volumeChapters.FirstOrDefault(c => c.ChapterNumber == currentChapterNumber - 3);

                var n2 = volumeChapters.FirstOrDefault(c => c.ChapterNumber == currentChapterNumber - 2);
                if (n2 != null && !string.IsNullOrWhiteSpace(n2.Hook))
                {
                    if (!hasAny) { sb.AppendLine("<adjacent_chapters>"); hasAny = true; }
                    sb.AppendLine($"<item name=\"第{n2.ChapterNumber}章 {n2.ChapterTitle}（上上章）\">");
                    sb.AppendLine($"结尾钉子：{n2.Hook}");
                    sb.AppendLine("</item>");
                    injectedCount++;
                }
                else if (n3 != null && !string.IsNullOrWhiteSpace(n3.Hook))
                {
                    if (!hasAny) { sb.AppendLine("<adjacent_chapters>"); hasAny = true; }
                    sb.AppendLine($"<item name=\"第{n3.ChapterNumber}章 {n3.ChapterTitle}（上上上章）\">");
                    sb.AppendLine($"结尾钉子：{n3.Hook}");
                    sb.AppendLine("</item>");
                    injectedCount++;
                }

                var n1 = volumeChapters.FirstOrDefault(c => c.ChapterNumber == currentChapterNumber - 1);
                if (n1 != null && (!string.IsNullOrWhiteSpace(n1.Hook) || !string.IsNullOrWhiteSpace(n1.MainPlotProgress)))
                {
                    if (!hasAny) { sb.AppendLine("<adjacent_chapters>"); hasAny = true; }
                    sb.AppendLine($"<item name=\"第{n1.ChapterNumber}章 {n1.ChapterTitle}（上章）\">");
                    if (!string.IsNullOrWhiteSpace(n1.Hook))
                        sb.AppendLine($"结尾钉子：{n1.Hook}");
                    if (!string.IsNullOrWhiteSpace(n1.MainPlotProgress))
                        sb.AppendLine($"主线推进点：{n1.MainPlotProgress}");
                    sb.AppendLine("</item>");
                    injectedCount++;
                }

                var p1 = volumeChapters.FirstOrDefault(c => c.ChapterNumber == currentChapterNumber + 1);
                if (p1 != null && (!string.IsNullOrWhiteSpace(p1.ChapterTheme) || !string.IsNullOrWhiteSpace(p1.MainGoal)))
                {
                    if (!hasAny) { sb.AppendLine("<adjacent_chapters>"); hasAny = true; }
                    sb.AppendLine($"<item name=\"第{p1.ChapterNumber}章 {p1.ChapterTitle}（下章）\">");
                    if (!string.IsNullOrWhiteSpace(p1.ChapterTheme))
                        sb.AppendLine($"章节主题：{p1.ChapterTheme}");
                    if (!string.IsNullOrWhiteSpace(p1.MainGoal))
                        sb.AppendLine($"章节主目标：{p1.MainGoal}");
                    sb.AppendLine("</item>");
                    injectedCount++;
                }

                var p2 = volumeChapters.FirstOrDefault(c => c.ChapterNumber == currentChapterNumber + 2);
                if (p2 != null && (!string.IsNullOrWhiteSpace(p2.ChapterTheme) || !string.IsNullOrWhiteSpace(p2.MainGoal)))
                {
                    if (!hasAny) { sb.AppendLine("<adjacent_chapters>"); hasAny = true; }
                    sb.AppendLine($"<item name=\"第{p2.ChapterNumber}章 {p2.ChapterTitle}（下下章）\">");
                    if (!string.IsNullOrWhiteSpace(p2.ChapterTheme))
                        sb.AppendLine($"章节主题：{p2.ChapterTheme}");
                    if (!string.IsNullOrWhiteSpace(p2.MainGoal))
                        sb.AppendLine($"章节主目标：{p2.MainGoal}");
                    sb.AppendLine("</item>");
                    injectedCount++;
                }

                if (hasAny)
                {
                    sb.AppendLine("</adjacent_chapters>");
                    TM.App.Log($"[ContextService] 蓝图窗口注入: 第{currentChapterNumber}章 -3/-2/-1/+1/+2, 包含 {injectedCount} 个邻近章节");
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildAdjacentChapterContextAsync失败: {ex.Message}"); }
            return sb.ToString();
        }

        public async Task<string> GetCoreDesignContextAsync()
        {
            if (TM.App.IsDebugMode)
                TM.App.Log("[ContextService] 构建CoreDesignContext");
            var (charMap, factionMap, locMap, chars, factions, locs) = await LoadEntityMapsAsync().ConfigureAwait(false);

            var creativeMaterialsTask = BuildCreativeMaterialsStringAsync(MaterialScope.Plot);
            var worldviewTask = BuildWorldviewStringAsync();
            var characterTask = BuildCharacterArcStringAsync(chars, charMap);
            var factionTask = BuildFactionSummaryStringAsync(factions, charMap);
            var locationTask = BuildLocationSummaryStringAsync(locs);
            var plotTask = BuildPlotRulesStringAsync(null, charMap, locMap);
            await Task.WhenAll(creativeMaterialsTask, worldviewTask, characterTask, factionTask, locationTask, plotTask).ConfigureAwait(false);

            var materialsResult = await creativeMaterialsTask.ConfigureAwait(false);
            var worldviewResult = await worldviewTask.ConfigureAwait(false);
            var charResult = await characterTask.ConfigureAwait(false);
            var factionResult = await factionTask.ConfigureAwait(false);
            var locResult = await locationTask.ConfigureAwait(false);
            var plotResult = await plotTask.ConfigureAwait(false);

            var totalChars = materialsResult.Length + worldviewResult.Length + charResult.Length + factionResult.Length + locResult.Length + plotResult.Length;
            if (totalChars > ContextCharBudget)
            {
                TM.App.Log($"[ContextService] OPT-017 CoreDesignContext 降级: {totalChars} chars > {ContextCharBudget}");
                charResult = await BuildCharacterStructureStringAsync(chars).ConfigureAwait(false);
                factionResult = await BuildFactionMinimalStringAsync().ConfigureAwait(false);
                plotResult = await BuildPlotRulesStructureStringAsync(null).ConfigureAwait(false);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_data>");
            sb.Append(materialsResult);
            sb.AppendLine();
            sb.Append(worldviewResult);
            sb.Append(charResult);
            sb.Append(factionResult);
            sb.Append(locResult);
            sb.Append(plotResult);
            sb.AppendLine("</design_data>");
            return sb.ToString();
        }

        public async Task<string> GetCoreDesignContextForVolumeAsync(string volumeKey)
        {
            if (TM.App.IsDebugMode)
                TM.App.Log($"[ContextService] 构建CoreDesignContext（按卷过滤：{volumeKey}）");
            var (charMap, factionMap, locMap, chars, factions, locs) = await LoadEntityMapsAsync().ConfigureAwait(false);

            var creativeMaterialsTask = BuildCreativeMaterialsStringAsync(MaterialScope.Plot);
            var worldviewTask = BuildWorldviewStringAsync();
            var volumeTask = GetVolumeDesignByCategoryAsync(volumeKey);
            await Task.WhenAll(creativeMaterialsTask, worldviewTask, volumeTask).ConfigureAwait(false);

            var volume = await volumeTask.ConfigureAwait(false);

            Task<string> charTask, factionBuildTask, locBuildTask;
            if (volume != null)
            {
                charTask = volume.ReferencedCharacterNames.Count > 0
                    ? BuildCharacterArcStringForVolumeAsync(volume.ReferencedCharacterNames, chars, charMap)
                    : BuildCharacterArcStringAsync(chars, charMap);
                factionBuildTask = volume.ReferencedFactionNames.Count > 0
                    ? BuildFactionSummaryStringForVolumeAsync(volume.ReferencedFactionNames, factions, charMap)
                    : BuildFactionSummaryStringAsync(factions, charMap);
                locBuildTask = volume.ReferencedLocationNames.Count > 0
                    ? BuildLocationSummaryStringForVolumeAsync(volume.ReferencedLocationNames, locs)
                    : BuildLocationSummaryStringAsync(locs);
            }
            else
            {
                charTask = BuildCharacterArcStringAsync(chars, charMap);
                factionBuildTask = BuildFactionSummaryStringAsync(factions, charMap);
                locBuildTask = BuildLocationSummaryStringAsync(locs);
            }
            var plotTask = BuildPlotRulesStringAsync(volume != null ? volumeKey : null, charMap, locMap);
            await Task.WhenAll(charTask, factionBuildTask, locBuildTask, plotTask).ConfigureAwait(false);

            var materialsResult = await creativeMaterialsTask.ConfigureAwait(false);
            var worldviewResult = await worldviewTask.ConfigureAwait(false);
            var charResult = await charTask.ConfigureAwait(false);
            var factionResult = await factionBuildTask.ConfigureAwait(false);
            var locResult = await locBuildTask.ConfigureAwait(false);
            var plotResult = await plotTask.ConfigureAwait(false);

            var totalChars = materialsResult.Length + worldviewResult.Length + charResult.Length + factionResult.Length + locResult.Length + plotResult.Length;
            if (totalChars > ContextCharBudget)
            {
                TM.App.Log($"[ContextService] OPT-017 CoreDesignContextForVolume 降级: {totalChars} chars > {ContextCharBudget}");
                charResult = await BuildCharacterStructureStringAsync(chars).ConfigureAwait(false);
                factionResult = await BuildFactionMinimalStringAsync().ConfigureAwait(false);
                plotResult = await BuildPlotRulesStructureStringAsync(volume != null ? volumeKey : null).ConfigureAwait(false);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_data>");
            sb.Append(materialsResult);
            sb.AppendLine();
            sb.Append(worldviewResult);
            sb.Append(charResult);
            sb.Append(factionResult);
            sb.Append(locResult);
            if (volume != null)
            {
                if (TM.App.IsDebugMode)
                    TM.App.Log($"[ContextService] 实体注入（按卷）: 角色名单={volume.ReferencedCharacterNames.Count}, 势力名单={volume.ReferencedFactionNames.Count}, 地点名单={volume.ReferencedLocationNames.Count}");
            }
            sb.Append(plotResult);
            sb.AppendLine("</design_data>");
            return sb.ToString();
        }

        private async Task<string> GetCoreDesignContextForEntityFilterAsync(
            IReadOnlyCollection<string>? charFilter,
            IReadOnlyCollection<string>? factionFilter,
            IReadOnlyCollection<string>? locFilter,
            string? volumeKeyForPlotRules)
        {
            if (TM.App.IsDebugMode)
                TM.App.Log($"[ContextService] 构建CoreDesignContext（精准过滤）: 角色={charFilter?.Count ?? -1}, 势力={factionFilter?.Count ?? -1}, 地点={locFilter?.Count ?? -1}");

            var (charMap, factionMap, locMap, chars, factions, locs) = await LoadEntityMapsAsync().ConfigureAwait(false);

            var creativeMaterialsTask = BuildCreativeMaterialsStringAsync(MaterialScope.Plot);
            var worldviewTask = BuildWorldviewStringAsync();
            var charTask = (charFilter?.Count > 0)
                ? BuildCharacterArcStringForVolumeAsync(charFilter, chars, charMap)
                : BuildCharacterArcStringAsync(chars, charMap);
            var factionBuildTask = (factionFilter?.Count > 0)
                ? BuildFactionSummaryStringForVolumeAsync(factionFilter, factions, charMap)
                : BuildFactionSummaryStringAsync(factions, charMap);
            var locBuildTask = (locFilter?.Count > 0)
                ? BuildLocationSummaryStringForVolumeAsync(locFilter, locs)
                : BuildLocationSummaryStringAsync(locs);
            var plotTask = BuildPlotRulesStringAsync(volumeKeyForPlotRules, charMap, locMap);
            await Task.WhenAll(creativeMaterialsTask, worldviewTask, charTask, factionBuildTask, locBuildTask, plotTask).ConfigureAwait(false);

            var materialsResult = await creativeMaterialsTask.ConfigureAwait(false);
            var worldviewResult = await worldviewTask.ConfigureAwait(false);
            var charResult = await charTask.ConfigureAwait(false);
            var factionResult = await factionBuildTask.ConfigureAwait(false);
            var locResult = await locBuildTask.ConfigureAwait(false);
            var plotResult = await plotTask.ConfigureAwait(false);

            var totalChars = materialsResult.Length + worldviewResult.Length + charResult.Length + factionResult.Length + locResult.Length + plotResult.Length;
            if (totalChars > ContextCharBudget)
            {
                TM.App.Log($"[ContextService] OPT-017 CoreDesignContextForEntityFilter 降级: {totalChars} chars > {ContextCharBudget}");
                charResult = await BuildCharacterStructureStringAsync(chars).ConfigureAwait(false);
                factionResult = await BuildFactionMinimalStringAsync().ConfigureAwait(false);
                plotResult = await BuildPlotRulesStructureStringAsync(volumeKeyForPlotRules).ConfigureAwait(false);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_data>");
            sb.Append(materialsResult);
            sb.AppendLine();
            sb.Append(worldviewResult);
            sb.Append(charResult);
            sb.Append(factionResult);
            sb.Append(locResult);
            sb.Append(plotResult);
            sb.AppendLine("</design_data>");
            return sb.ToString();
        }

        private async Task<string> BuildPlotRulesStringAsync(
            string? volumeFilter,
            Dictionary<string, string>? charIdToName = null,
            Dictionary<string, string>? locIdToName = null)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var plotRules = await LoadFunctionDataAsync<PlotRulesData>("PlotRules").ConfigureAwait(false);
                var enabledRules = plotRules.Where(i => i.IsEnabled && HasPlotContent(i)).ToList();
                if (charIdToName == null)
                {
                    var cr = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules").ConfigureAwait(false);
                    charIdToName = cr.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                        .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
                }
                if (locIdToName == null)
                {
                    var lr = await LoadFunctionDataAsync<LocationRulesData>("LocationRules").ConfigureAwait(false);
                    locIdToName = lr.Where(l => !string.IsNullOrWhiteSpace(l.Id))
                        .ToDictionary(l => l.Id, l => l.Name, StringComparer.OrdinalIgnoreCase);
                }
                if (string.IsNullOrWhiteSpace(volumeFilter))
                {
                    sb.AppendLine("<plot_rules>");
                    foreach (var item in enabledRules)
                        AppendPlotRuleFull(sb, item, charIdToName, locIdToName);
                }
                else
                {
                    var filterVolNum = ExtractVolumeNumberFromText(volumeFilter);
                    var injected = enabledRules.Where(r =>
                        r.EventType == "主线剧情" ||
                        r.AssignedVolume == "全局" ||
                        string.IsNullOrWhiteSpace(r.AssignedVolume) ||
                        string.Equals(r.AssignedVolume, volumeFilter, StringComparison.OrdinalIgnoreCase) ||
                        (filterVolNum > 0 && ExtractVolumeNumberFromText(r.AssignedVolume) == filterVolNum)
                    ).ToList();
                    var skipped = enabledRules.Count - injected.Count;
                    sb.AppendLine($"<plot_rules volume=\"{volumeFilter}\">");
                    foreach (var item in injected)
                        AppendPlotRuleFull(sb, item, charIdToName, locIdToName);
                    if (skipped > 0) sb.AppendLine($"（另有 {skipped} 条其他卷剧情未注入）");
                    if (TM.App.IsDebugMode)
                        TM.App.Log($"[ContextService] 剧情按卷过滤: 注入={injected.Count}, 跳过={skipped}");
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] 加载PlotRules失败: {ex.Message}"); }
            sb.AppendLine("</plot_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildCharacterStructureStringAsync(
            List<CharacterRulesData>? preloaded = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<character_rules>");
            try
            {
                var characterRules = preloaded ?? await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules").ConfigureAwait(false);
                foreach (var item in characterRules.Where(i => i.IsEnabled && HasCharacterContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.CharacterType)) sb.AppendLine($"角色类型：{item.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(item.Identity)) sb.AppendLine($"身份：{item.Identity}");
                    if (!string.IsNullOrWhiteSpace(item.Want)) sb.AppendLine($"外在目标：{item.Want}");
                    if (!string.IsNullOrWhiteSpace(item.Need)) sb.AppendLine($"内在需求：{item.Need}");
                    if (!string.IsNullOrWhiteSpace(item.FlawBelief)) sb.AppendLine($"致命缺点：{item.FlawBelief}");
                    if (!string.IsNullOrWhiteSpace(item.GrowthPath)) sb.AppendLine($"成长路径：{item.GrowthPath}");
                    if (!string.IsNullOrWhiteSpace(item.Relationships)) sb.AppendLine($"关系：{item.Relationships}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildCharacterStructureStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</character_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildPlotRulesStructureStringAsync(string? volumeFilter)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var plotRules = await LoadFunctionDataAsync<PlotRulesData>("PlotRules").ConfigureAwait(false);
                var enabledRules = plotRules.Where(i => i.IsEnabled && HasPlotContent(i)).ToList();

                List<PlotRulesData> injected;
                if (string.IsNullOrWhiteSpace(volumeFilter))
                {
                    sb.AppendLine("<plot_rules>");
                    injected = enabledRules;
                }
                else
                {
                    var filterVolNum = ExtractVolumeNumberFromText(volumeFilter);
                    injected = enabledRules.Where(r =>
                        r.EventType == "主线剧情" ||
                        r.AssignedVolume == "全局" ||
                        string.IsNullOrWhiteSpace(r.AssignedVolume) ||
                        string.Equals(r.AssignedVolume, volumeFilter, StringComparison.OrdinalIgnoreCase) ||
                        (filterVolNum > 0 && ExtractVolumeNumberFromText(r.AssignedVolume) == filterVolNum)
                    ).ToList();
                    var skipped = enabledRules.Count - injected.Count;
                    sb.AppendLine($"<plot_rules volume=\"{volumeFilter}\">");
                    if (skipped > 0) sb.AppendLine($"（另有 {skipped} 条其他卷剧情未注入）");
                }

                foreach (var item in injected)
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.TargetVolume)) sb.AppendLine($"全书总卷数：{item.TargetVolume}");
                    if (!string.IsNullOrWhiteSpace(item.AssignedVolume)) sb.AppendLine($"所属卷：{item.AssignedVolume}");
                    if (!string.IsNullOrWhiteSpace(item.OneLineSummary)) sb.AppendLine($"简介：{item.OneLineSummary}");
                    if (!string.IsNullOrWhiteSpace(item.EventType)) sb.AppendLine($"事件类型：{item.EventType}");
                    if (!string.IsNullOrWhiteSpace(item.StoryPhase)) sb.AppendLine($"所属阶段：{item.StoryPhase}");
                    if (!string.IsNullOrWhiteSpace(item.Goal)) sb.AppendLine($"目标：{item.Goal}");
                    if (!string.IsNullOrWhiteSpace(item.Conflict)) sb.AppendLine($"冲突：{item.Conflict}");
                    if (!string.IsNullOrWhiteSpace(item.MainPlotPush)) sb.AppendLine($"主线推动：{item.MainPlotPush}");
                    if (!string.IsNullOrWhiteSpace(item.CharacterGrowth)) sb.AppendLine($"角色成长：{item.CharacterGrowth}");
                    if (!string.IsNullOrWhiteSpace(item.Result)) sb.AppendLine($"结果：{item.Result}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildPlotRulesStructureStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</plot_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildPlotRulesOutlineMinStringAsync(string? volumeFilter)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var plotRules = await LoadFunctionDataAsync<PlotRulesData>("PlotRules").ConfigureAwait(false);
                var enabledRules = plotRules.Where(i => i.IsEnabled && HasPlotContent(i)).ToList();

                List<PlotRulesData> injected;
                if (string.IsNullOrWhiteSpace(volumeFilter))
                {
                    sb.AppendLine("<plot_rules>");
                    injected = enabledRules;
                }
                else
                {
                    var filterVolNum = ExtractVolumeNumberFromText(volumeFilter);
                    injected = enabledRules.Where(r =>
                        r.EventType == "主线剧情" ||
                        r.AssignedVolume == "全局" ||
                        string.IsNullOrWhiteSpace(r.AssignedVolume) ||
                        string.Equals(r.AssignedVolume, volumeFilter, StringComparison.OrdinalIgnoreCase) ||
                        (filterVolNum > 0 && ExtractVolumeNumberFromText(r.AssignedVolume) == filterVolNum)
                    ).ToList();
                    var skipped = enabledRules.Count - injected.Count;
                    sb.AppendLine($"<plot_rules volume=\"{volumeFilter}\">");
                    if (skipped > 0) sb.AppendLine($"（另有 {skipped} 条其他卷剧情未注入）");
                }

                foreach (var item in injected)
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.AssignedVolume)) sb.AppendLine($"所属卷：{item.AssignedVolume}");
                    if (!string.IsNullOrWhiteSpace(item.EventType)) sb.AppendLine($"事件类型：{item.EventType}");
                    if (!string.IsNullOrWhiteSpace(item.OneLineSummary)) sb.AppendLine($"简介：{item.OneLineSummary}");
                    if (!string.IsNullOrWhiteSpace(item.Goal)) sb.AppendLine($"目标：{item.Goal}");
                    if (!string.IsNullOrWhiteSpace(item.StoryPhase)) sb.AppendLine($"所属阶段：{item.StoryPhase}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildPlotRulesOutlineMinStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</plot_rules>");
            return sb.ToString();
        }

        private void AppendPlotRuleFull(
            System.Text.StringBuilder sb,
            PlotRulesData item,
            Dictionary<string, string> charIdToName,
            Dictionary<string, string> locIdToName)
        {
            sb.AppendLine($"<item name=\"{item.Name}\">");
            if (!string.IsNullOrWhiteSpace(item.TargetVolume)) sb.AppendLine($"全书总卷数：{item.TargetVolume}");
            if (!string.IsNullOrWhiteSpace(item.AssignedVolume)) sb.AppendLine($"所属卷：{item.AssignedVolume}");
            if (!string.IsNullOrWhiteSpace(item.OneLineSummary)) sb.AppendLine($"简介：{item.OneLineSummary}");
            if (!string.IsNullOrWhiteSpace(item.EventType)) sb.AppendLine($"事件类型：{item.EventType}");
            if (!string.IsNullOrWhiteSpace(item.StoryPhase)) sb.AppendLine($"所属阶段：{item.StoryPhase}");
            if (!string.IsNullOrWhiteSpace(item.PrerequisitesTrigger)) sb.AppendLine($"前置条件：{item.PrerequisitesTrigger}");
            if (!string.IsNullOrWhiteSpace(item.MainCharacters)) sb.AppendLine($"主要角色：{ResolveIds(item.MainCharacters, charIdToName)}");
            if (!string.IsNullOrWhiteSpace(item.KeyNpcs)) sb.AppendLine($"关键NPC：{ResolveIds(item.KeyNpcs, charIdToName)}");
            if (!string.IsNullOrWhiteSpace(item.Location)) sb.AppendLine($"地点：{ResolveId(item.Location, locIdToName)}");
            if (!string.IsNullOrWhiteSpace(item.TimeDuration)) sb.AppendLine($"时间跨度：{item.TimeDuration}");
            if (!string.IsNullOrWhiteSpace(item.StepTitle)) sb.AppendLine($"步骤标题：{item.StepTitle}");
            if (!string.IsNullOrWhiteSpace(item.Goal)) sb.AppendLine($"目标：{item.Goal}");
            if (!string.IsNullOrWhiteSpace(item.Conflict)) sb.AppendLine($"冲突：{item.Conflict}");
            if (!string.IsNullOrWhiteSpace(item.Result)) sb.AppendLine($"结果：{item.Result}");
            if (!string.IsNullOrWhiteSpace(item.EmotionCurve)) sb.AppendLine($"情绪曲线：{item.EmotionCurve}");
            if (!string.IsNullOrWhiteSpace(item.MainPlotPush)) sb.AppendLine($"主线推动：{item.MainPlotPush}");
            if (!string.IsNullOrWhiteSpace(item.CharacterGrowth)) sb.AppendLine($"角色成长：{item.CharacterGrowth}");
            if (!string.IsNullOrWhiteSpace(item.WorldReveal)) sb.AppendLine($"世界观揭示：{item.WorldReveal}");
            if (!string.IsNullOrWhiteSpace(item.RewardsClues)) sb.AppendLine($"奖励/线索：{item.RewardsClues}");
            sb.AppendLine("</item>");
            sb.AppendLine();
        }

    }
}


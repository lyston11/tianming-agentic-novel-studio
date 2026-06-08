using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class PreflightValidator
    {
        private readonly Func<string, bool>? _isModuleEnabled;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly char[] NameSeparators = new[] { ',', '，', '、', '\n', '\r', ' ', '\t', ';', '；' };

        private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "无", "暂无", "空", "-", "/", "none", "n/a", "null",
            "无角色", "无地点", "无势力", "无物品", "未定", "待定", "未知",
            "不详", "略", "省略", "没有", "无相关", "无关联"
        };

        public PreflightValidator(Func<string, bool>? isModuleEnabled = null)
        {
            _isModuleEnabled = isModuleEnabled;
        }

        public class PreflightResult
        {
            public bool IsValid { get; set; }
            public List<string> Errors { get; } = new();
            public List<string> Warnings { get; } = new();

            public string Summary
            {
                get
                {
                    if (Errors.Count == 0 && Warnings.Count == 0) return "全部检查通过";
                    var parts = new List<string>();
                    if (Errors.Count > 0) parts.Add($"{Errors.Count} 项错误");
                    if (Warnings.Count > 0) parts.Add($"{Warnings.Count} 项警告");
                    return string.Join("，", parts);
                }
            }
        }

        public async Task<PreflightResult> RunAsync()
        {
            var result = new PreflightResult();
            try
            {
                var charactersTask = LoadAllAsync<CharacterRulesData>("Design/Elements/CharacterRules");
                var locationsTask = LoadAllAsync<LocationRulesData>("Design/Elements/LocationRules");
                var factionsTask = LoadAllAsync<FactionRulesData>("Design/Elements/FactionRules");
                var plotRulesTask = LoadAllAsync<PlotRulesData>("Design/Elements/PlotRules");
                var volumesTask = LoadAllAsync<VolumeDesignData>("Generate/Elements/VolumeDesign");
                var chaptersTask = LoadAllAsync<ChapterData>("Generate/Elements/Chapter");
                var blueprintsTask = LoadAllAsync<BlueprintData>("Generate/Elements/Blueprint");

                await Task.WhenAll(charactersTask, locationsTask, factionsTask, plotRulesTask,
                    volumesTask, chaptersTask, blueprintsTask).ConfigureAwait(false);

                var characters = await charactersTask.ConfigureAwait(false);
                var locations = await locationsTask.ConfigureAwait(false);
                var factions = await factionsTask.ConfigureAwait(false);
                var plotRules = await plotRulesTask.ConfigureAwait(false);
                var volumes = await volumesTask.ConfigureAwait(false);
                var chapters = await chaptersTask.ConfigureAwait(false);
                var blueprints = await blueprintsTask.ConfigureAwait(false);

                var charIndex = BuildIndex(characters, c => c.Name, c => c.Id);
                var locIndex = BuildIndex(locations, l => l.Name, l => l.Id);
                var facIndex = BuildIndex(factions, f => f.Name, f => f.Id);

                ValidateVolumeReferences(volumes, charIndex, facIndex, locIndex, result);

                ValidateChapterReferences(chapters, charIndex, facIndex, locIndex, result);

                ValidateBlueprintReferences(blueprints, charIndex, facIndex, locIndex, result);

                ValidateVolumeChapterRelation(volumes, chapters, result);

                ValidateChapterBlueprintRelation(chapters, blueprints, result);

                ValidateUniqueness(chapters, blueprints, result);

                if (result.Errors.Count == 0)
                {
                    ValidateHierarchicalAlignment(volumes, chapters, blueprints, charIndex, facIndex, locIndex, result);
                }

                result.IsValid = result.Errors.Count == 0;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"预检异常（无法继续打包）: {ex.Message}");
                TM.App.Log($"[PreflightValidator] 预检异常: {ex}");
            }

            return result;
        }

        private class EntityIndex
        {
            public HashSet<string> Names { get; }
            public HashSet<string> Ids { get; }
            public Dictionary<string, string> NameToId { get; }

            public EntityIndex(HashSet<string> names, HashSet<string> ids, Dictionary<string, string> nameToId)
            {
                Names = names;
                Ids = ids;
                NameToId = nameToId;
            }

            public string NormalizeToId(string nameOrId)
            {
                var trimmed = nameOrId?.Trim() ?? string.Empty;
                if (NameToId.TryGetValue(trimmed, out var id)) return id;
                if (Ids.Contains(trimmed)) return trimmed;
                return trimmed;
            }
        }

        private static EntityIndex BuildIndex<T>(
            List<T> entities, Func<T, string> nameSelector, Func<T, string> idSelector)
        {
            var names = new HashSet<string>(
                entities.Select(nameSelector).Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(
                entities.Select(idSelector).Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);
            var nameToId = entities
                .Where(e => !string.IsNullOrWhiteSpace(nameSelector(e)) && !string.IsNullOrWhiteSpace(idSelector(e)))
                .GroupBy(e => nameSelector(e), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => idSelector(g.First()), StringComparer.OrdinalIgnoreCase);
            return new EntityIndex(names, ids, nameToId);
        }

        private static List<string> SplitNames(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && !IgnoredNames.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> FindUnmatched(IEnumerable<string>? names, EntityIndex index)
        {
            if (names == null) return new List<string>();
            return names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Where(n => !index.Names.Contains(n) && !index.Ids.Contains(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void ValidateVolumeReferences(
            List<VolumeDesignData> volumes,
            EntityIndex charIndex,
            EntityIndex facIndex,
            EntityIndex locIndex,
            PreflightResult result)
        {
            foreach (var v in volumes)
            {
                if (v.VolumeNumber <= 0) continue;
                var label = $"第{v.VolumeNumber}卷";

                var unmatchedChars = FindUnmatched(v.ReferencedCharacterNames, charIndex);
                if (unmatchedChars.Count > 0)
                    result.Errors.Add($"{label}「涉及角色」存在未映射名称: {string.Join("、", unmatchedChars)}");

                var unmatchedFacs = FindUnmatched(v.ReferencedFactionNames, facIndex);
                if (unmatchedFacs.Count > 0)
                    result.Errors.Add($"{label}「涉及势力」存在未映射名称: {string.Join("、", unmatchedFacs)}");

                var unmatchedLocs = FindUnmatched(v.ReferencedLocationNames, locIndex);
                if (unmatchedLocs.Count > 0)
                    result.Errors.Add($"{label}「涉及地点」存在未映射名称: {string.Join("、", unmatchedLocs)}");
            }
        }

        private static void ValidateChapterReferences(
            List<ChapterData> chapters,
            EntityIndex charIndex,
            EntityIndex facIndex,
            EntityIndex locIndex,
            PreflightResult result)
        {
            foreach (var c in chapters)
            {
                var label = !string.IsNullOrWhiteSpace(c.Volume)
                    ? $"{c.Volume}·第{c.ChapterNumber}章"
                    : $"章节[{c.ChapterTitle ?? c.Name}]";

                var unmatchedChars = FindUnmatched(c.ReferencedCharacterNames, charIndex);
                if (unmatchedChars.Count > 0)
                    result.Errors.Add($"{label}「出场角色」存在未映射名称: {string.Join("、", unmatchedChars)}");

                var unmatchedFacs = FindUnmatched(c.ReferencedFactionNames, facIndex);
                if (unmatchedFacs.Count > 0)
                    result.Errors.Add($"{label}「涉及势力」存在未映射名称: {string.Join("、", unmatchedFacs)}");

                var unmatchedLocs = FindUnmatched(c.ReferencedLocationNames, locIndex);
                if (unmatchedLocs.Count > 0)
                    result.Errors.Add($"{label}「涉及地点」存在未映射名称: {string.Join("、", unmatchedLocs)}");
            }
        }

        private static void ValidateBlueprintReferences(
            List<BlueprintData> blueprints,
            EntityIndex charIndex,
            EntityIndex facIndex,
            EntityIndex locIndex,
            PreflightResult result)
        {
            foreach (var b in blueprints)
            {
                var label = !string.IsNullOrWhiteSpace(b.ChapterId)
                    ? $"{b.ChapterId}·场景{b.SceneNumber}"
                    : $"蓝图[{b.SceneTitle ?? b.Name}]";

                var unmatchedCast = FindUnmatched(SplitNames(b.Cast), charIndex);
                if (unmatchedCast.Count > 0)
                    result.Errors.Add($"{label}「出场角色」存在未映射名称: {string.Join("、", unmatchedCast)}");

                var unmatchedLocs = FindUnmatched(SplitNames(b.Locations), locIndex);
                if (unmatchedLocs.Count > 0)
                    result.Errors.Add($"{label}「涉及地点」存在未映射名称: {string.Join("、", unmatchedLocs)}");

                var unmatchedFacs = FindUnmatched(SplitNames(b.Factions), facIndex);
                if (unmatchedFacs.Count > 0)
                    result.Errors.Add($"{label}「涉及势力」存在未映射名称: {string.Join("、", unmatchedFacs)}");
            }
        }

        private static void ValidateVolumeChapterRelation(
            List<VolumeDesignData> volumes,
            List<ChapterData> chapters,
            PreflightResult result)
        {
            var volumeNumbers = new HashSet<int>(volumes.Where(v => v.VolumeNumber > 0).Select(v => v.VolumeNumber));
            if (volumeNumbers.Count == 0) return;

            foreach (var c in chapters)
            {
                if (string.IsNullOrWhiteSpace(c.Volume))
                {
                    result.Errors.Add($"章节[{c.ChapterTitle ?? c.Name}]「所属卷」字段为空，无法关联到分卷设计");
                    continue;
                }

                if (!TryParseVolumeNumber(c.Volume, out var volNum) || !volumeNumbers.Contains(volNum))
                {
                    result.Errors.Add($"章节[{c.ChapterTitle ?? c.Name}]「所属卷」({c.Volume}) 无法匹配到任何分卷设计");
                }
            }
        }

        private static void ValidateChapterBlueprintRelation(
            List<ChapterData> chapters,
            List<BlueprintData> blueprints,
            PreflightResult result)
        {
            var chapterIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in chapters)
            {
                if (c.ChapterNumber <= 0 || string.IsNullOrWhiteSpace(c.Volume)) continue;
                if (TryParseVolumeNumber(c.Volume, out var vn))
                {
                    chapterIdSet.Add($"vol{vn}_ch{c.ChapterNumber}");
                }
            }

            foreach (var b in blueprints)
            {
                if (string.IsNullOrWhiteSpace(b.ChapterId))
                {
                    result.Errors.Add($"蓝图[{b.SceneTitle ?? b.Name}]「关联章节ID」为空");
                    continue;
                }

                var parsed = ChapterParserHelper.ParseChapterId(b.ChapterId);
                if (parsed == null)
                {
                    result.Errors.Add($"蓝图[{b.SceneTitle ?? b.Name}]「关联章节ID」格式错误: {b.ChapterId}（应为 vol{{N}}_ch{{M}}）");
                    continue;
                }

                if (!chapterIdSet.Contains(b.ChapterId))
                {
                    result.Errors.Add($"蓝图[{b.SceneTitle ?? b.Name}]「关联章节ID」({b.ChapterId}) 没有对应的章节设计");
                }
            }
        }

        private static void ValidateUniqueness(
            List<ChapterData> chapters,
            List<BlueprintData> blueprints,
            PreflightResult result)
        {
            var chapterDupGroups = new Dictionary<(int VolNum, int ChapterNum), List<string>>();
            foreach (var c in chapters)
            {
                if (c.ChapterNumber <= 0 || string.IsNullOrWhiteSpace(c.Volume)) continue;
                if (!TryParseVolumeNumber(c.Volume, out var volNum)) continue;

                var key = (volNum, c.ChapterNumber);
                if (!chapterDupGroups.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    chapterDupGroups[key] = list;
                }
                list.Add(string.IsNullOrWhiteSpace(c.ChapterTitle) ? (c.Name ?? c.Id) : c.ChapterTitle);
            }
            foreach (var (key, names) in chapterDupGroups)
            {
                if (names.Count > 1)
                {
                    result.Errors.Add(
                        $"第{key.VolNum}卷·第{key.ChapterNum}章存在 {names.Count} 条重复章节定义：{string.Join("、", names)}。" +
                        "同一卷的章节号必须唯一，请删除多余项后重新打包。");
                }
            }

            var blueprintDupGroups = new Dictionary<(string ChapterId, int SceneNum), List<string>>(
                EqualityComparer<(string, int)>.Default);
            foreach (var b in blueprints)
            {
                if (string.IsNullOrWhiteSpace(b.ChapterId) || b.SceneNumber <= 0) continue;
                var key = (b.ChapterId.Trim(), b.SceneNumber);
                if (!blueprintDupGroups.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    blueprintDupGroups[key] = list;
                }
                list.Add(string.IsNullOrWhiteSpace(b.SceneTitle) ? (b.Name ?? b.Id) : b.SceneTitle);
            }
            foreach (var (key, names) in blueprintDupGroups)
            {
                if (names.Count > 1)
                {
                    result.Errors.Add(
                        $"{key.ChapterId}·场景{key.SceneNum} 存在 {names.Count} 条重复蓝图定义：{string.Join("、", names)}。" +
                        "同一章节的场景号必须唯一，请删除多余项后重新打包。");
                }
            }
        }

        private static void ValidateHierarchicalAlignment(
            List<VolumeDesignData> volumes,
            List<ChapterData> chapters,
            List<BlueprintData> blueprints,
            EntityIndex charIndex, EntityIndex facIndex, EntityIndex locIndex,
            PreflightResult result)
        {
            var volumeRefs = new Dictionary<int, (HashSet<string> Chars, HashSet<string> Locs, HashSet<string> Facs)>();
            foreach (var v in volumes)
            {
                if (v.VolumeNumber <= 0) continue;
                volumeRefs[v.VolumeNumber] = (
                    NormalizeToIdSet(v.ReferencedCharacterNames, charIndex),
                    NormalizeToIdSet(v.ReferencedLocationNames, locIndex),
                    NormalizeToIdSet(v.ReferencedFactionNames, facIndex)
                );
            }

            var chapterRefs = new Dictionary<string, (int VolNum, HashSet<string> Chars, HashSet<string> Locs, HashSet<string> Facs)>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in chapters)
            {
                if (c.ChapterNumber <= 0 || string.IsNullOrWhiteSpace(c.Volume)) continue;
                if (!TryParseVolumeNumber(c.Volume, out var volNum)) continue;

                var chapterId = $"vol{volNum}_ch{c.ChapterNumber}";
                var chapterChars = NormalizeToIdSet(c.ReferencedCharacterNames, charIndex);
                var chapterLocs = NormalizeToIdSet(c.ReferencedLocationNames, locIndex);
                var chapterFacs = NormalizeToIdSet(c.ReferencedFactionNames, facIndex);
                chapterRefs[chapterId] = (volNum, chapterChars, chapterLocs, chapterFacs);

                if (volumeRefs.TryGetValue(volNum, out var volRefs))
                {
                    var chapterLabel = $"第{volNum}卷·第{c.ChapterNumber}章";
                    if (volRefs.Chars.Count > 0)
                        CheckSubset(chapterChars, volRefs.Chars, chapterLabel, "卷", charIndex, "出场角色", result);
                    if (volRefs.Locs.Count > 0)
                        CheckSubset(chapterLocs, volRefs.Locs, chapterLabel, "卷", locIndex, "涉及地点", result);
                    if (volRefs.Facs.Count > 0)
                        CheckSubset(chapterFacs, volRefs.Facs, chapterLabel, "卷", facIndex, "涉及势力", result);
                }
            }

            foreach (var b in blueprints)
            {
                if (string.IsNullOrWhiteSpace(b.ChapterId)) continue;
                if (!chapterRefs.TryGetValue(b.ChapterId, out var chRefs)) continue;

                var bpChars = NormalizeToIdSet(SplitNames(b.Cast), charIndex);
                var bpLocs = NormalizeToIdSet(SplitNames(b.Locations), locIndex);
                var bpFacs = NormalizeToIdSet(SplitNames(b.Factions), facIndex);

                var label = $"{b.ChapterId}·场景{b.SceneNumber}";
                if (chRefs.Chars.Count > 0)
                    CheckSubset(bpChars, chRefs.Chars, label, "章节", charIndex, "出场角色", result);
                if (chRefs.Locs.Count > 0)
                    CheckSubset(bpLocs, chRefs.Locs, label, "章节", locIndex, "涉及地点", result);
                if (chRefs.Facs.Count > 0)
                    CheckSubset(bpFacs, chRefs.Facs, label, "章节", facIndex, "涉及势力", result);
            }
        }

        private static HashSet<string> NormalizeToIdSet(IEnumerable<string>? names, EntityIndex index)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names == null) return ids;
            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                var trimmed = n.Trim();
                if (index.NameToId.TryGetValue(trimmed, out var id)) ids.Add(id);
                else if (index.Ids.Contains(trimmed)) ids.Add(trimmed);
            }
            return ids;
        }

        private static void CheckSubset(
            HashSet<string> sub,
            HashSet<string> sup,
            string subLabel,
            string supLabel,
            EntityIndex index,
            string field,
            PreflightResult result)
        {
            if (sub.Count == 0) return;
            var escaped = sub.Where(id => !sup.Contains(id)).ToList();
            if (escaped.Count == 0) return;

            var idToName = index.NameToId
                .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);
            var displayed = escaped.Select(id => idToName.TryGetValue(id, out var name) ? name : id).ToList();

            result.Errors.Add(
                $"{subLabel}「{field}」存在 {{{string.Join("、", displayed)}}} 未在所属{supLabel}中声明" +
                $"（必须先在{supLabel}级「{field}」中加入这些项，然后再在{subLabel}级使用）");
        }

        private async Task<List<T>> LoadAllAsync<T>(string relativePath)
        {
            var modulePath = GetModulePathFromRelativePath(relativePath);
            if (!string.IsNullOrEmpty(modulePath) && _isModuleEnabled != null && !_isModuleEnabled(modulePath))
                return new List<T>();

            var basePath = Path.Combine(StoragePathHelper.GetStorageRoot(), "Modules", relativePath);
            var items = new List<T>();
            if (!Directory.Exists(basePath)) return items;

            foreach (var file in Directory.GetFiles(basePath, "*.json", SearchOption.AllDirectories))
            {
                var fn = Path.GetFileName(file);
                if (string.Equals(fn, "categories.json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fn, "built_in_categories.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                    var list = JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
                    if (list == null) continue;

                    foreach (var item in list)
                    {
                        if (item == null) continue;
                        if (item is IEnableable enableable && !enableable.IsEnabled) continue;
                        items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PreflightValidator] 读取文件失败 [{file}]: {ex.Message}");
                }
            }

            return items;
        }

        private static bool TryParseVolumeNumber(string volumeText, out int volNum)
        {
            volNum = 0;
            if (string.IsNullOrWhiteSpace(volumeText)) return false;

            var trimmed = volumeText.Trim();
            var idx1 = trimmed.IndexOf('第');
            var idx2 = trimmed.IndexOf('卷');
            if (idx1 != 0 || idx2 <= idx1) return false;

            var inner = trimmed.AsSpan(idx1 + 1, idx2 - idx1 - 1).Trim().ToString();
            if (inner.Length == 0) return false;

            if (int.TryParse(inner, out var arabic) && arabic > 0)
            {
                volNum = arabic;
                return true;
            }

            if (TryParseChineseNumber(inner, out var chinese) && chinese > 0)
            {
                volNum = chinese;
                return true;
            }

            return false;
        }

        private static bool TryParseChineseNumber(string s, out int n)
        {
            n = 0;
            if (string.IsNullOrEmpty(s)) return false;

            var result = 0;
            var current = 0;
            foreach (var c in s)
            {
                var digit = c switch
                {
                    '零' or '〇' => 0,
                    '一' or '壹' => 1,
                    '二' or '两' or '贰' => 2,
                    '三' or '叁' => 3,
                    '四' or '肆' => 4,
                    '五' or '伍' => 5,
                    '六' or '陆' => 6,
                    '七' or '柒' => 7,
                    '八' or '捌' => 8,
                    '九' or '玖' => 9,
                    _ => -1
                };
                if (digit >= 0)
                {
                    current = digit;
                    continue;
                }

                var unit = c switch
                {
                    '十' or '拾' => 10,
                    '百' or '佰' => 100,
                    '千' or '仟' => 1000,
                    '万' or '萬' => 10000,
                    _ => 0
                };
                if (unit == 0) return false;

                if (current == 0) current = 1;
                result += current * unit;
                current = 0;
            }

            n = result + current;
            return n > 0;
        }

        private static string GetModulePathFromRelativePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return string.Empty;
            var parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length < 2 ? string.Empty : $"{parts[0]}/{parts[1]}";
        }
    }
}

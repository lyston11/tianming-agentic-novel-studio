using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Plot;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideIndexBuilder
    {
        private static readonly System.Text.RegularExpressions.Regex VolumeNumberRegex = new(@"vol[_\-]?(\d+)", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        #region 辅助方法

        private static List<string> ResolveEntityIds<T>(
            List<string> names,
            List<T> entities,
            Func<T, string> nameSelector,
            Func<T, string> idSelector)
        {
            if (names == null || names.Count == 0)
                return new List<string>();

            var nameToId = entities
                .Where(e => !string.IsNullOrWhiteSpace(nameSelector(e)) && !string.IsNullOrWhiteSpace(idSelector(e)))
                .GroupBy(e => nameSelector(e), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => idSelector(g.First()), StringComparer.OrdinalIgnoreCase);
            var idSet = new HashSet<string>(
                entities
                    .Select(idSelector)
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);

            return names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n =>
                {
                    var trimmed = n.Trim();
                    if (idSet.Contains(trimmed)) return trimmed;
                    return nameToId.TryGetValue(trimmed, out var id) ? id : null;
                })
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct()
                .ToList();
        }

        private static List<string> GetUnmatchedEntityNames<T>(
            List<string> names,
            List<T> entities,
            Func<T, string> nameSelector,
            Func<T, string> idSelector)
        {
            if (names == null || names.Count == 0)
                return new List<string>();

            var nameSet = new HashSet<string>(
                entities
                    .Where(e => !string.IsNullOrWhiteSpace(nameSelector(e)))
                    .Select(e => nameSelector(e)),
                StringComparer.OrdinalIgnoreCase);
            var idSet = new HashSet<string>(
                entities
                    .Where(e => !string.IsNullOrWhiteSpace(idSelector(e)))
                    .Select(e => idSelector(e)),
                StringComparer.OrdinalIgnoreCase);

            return names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Where(n => !nameSet.Contains(n) && !idSet.Contains(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> MatchPlotRulesByCharacters(
            string chapterId,
            List<string> characterIds,
            List<CharacterRulesData> allCharacters,
            List<PlotRulesData> allPlotRules,
            Dictionary<string, List<string>>? mainCharsSplitCache = null)
        {
            if (allPlotRules.Count == 0) return new List<string>();

            var characterIdHashSet = characterIds == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(characterIds, StringComparer.OrdinalIgnoreCase);

            var characterNameHashSet = new HashSet<string>(
                allCharacters
                    .Where(c => characterIdHashSet.Contains(c.Id) && !string.IsNullOrWhiteSpace(c.Name))
                    .Select(c => c.Name),
                StringComparer.OrdinalIgnoreCase);

            var result = new List<string>();
            foreach (var plotRule in allPlotRules)
            {
                var storyPhase = plotRule.StoryPhase?.Trim();
                var isChapterIdPhase = !string.IsNullOrEmpty(storyPhase)
                    && ChapterParserHelper.ParseChapterId(storyPhase).HasValue;

                if (isChapterIdPhase)
                {
                    if (string.Equals(storyPhase, chapterId, StringComparison.Ordinal))
                    {
                        result.Add(plotRule.Id);
                    }
                    continue;
                }

                if (characterIdHashSet.Count == 0) continue;

                IEnumerable<string> mainChars;
                if (mainCharsSplitCache != null && mainCharsSplitCache.TryGetValue(plotRule.Id, out var cached))
                {
                    mainChars = cached;
                }
                else
                {
                    mainChars = (plotRule.MainCharacters ?? string.Empty)
                        .Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim());
                }

                if (mainChars.Any(c => characterIdHashSet.Contains(c) || characterNameHashSet.Contains(c)))
                {
                    result.Add(plotRule.Id);
                }
            }

            return result.Distinct().ToList();
        }

        private ReverseIndex BuildReverseIndex(Dictionary<string, ChapterGuideEntry> chapters)
        {
            var reverseIndex = new ReverseIndex();

            foreach (var (chapterId, entry) in chapters)
            {
                foreach (var charId in entry.ContextIds.Characters)
                {
                    if (!reverseIndex.ByCharacter.TryGetValue(charId, out var charList))
                    {
                        charList = new List<string>();
                        reverseIndex.ByCharacter[charId] = charList;
                    }
                    charList.Add(chapterId);
                }

                foreach (var locId in entry.ContextIds.Locations)
                {
                    if (!reverseIndex.ByLocation.TryGetValue(locId, out var locList))
                    {
                        locList = new List<string>();
                        reverseIndex.ByLocation[locId] = locList;
                    }
                    locList.Add(chapterId);
                }
            }

            return reverseIndex;
        }

        private async Task<List<T>> LoadAllAsync<T>(string relativePath)
        {
            var cacheKey = $"{typeof(T).FullName}|{relativePath}";
            if (_loadCache.TryGetValue(cacheKey, out var cached))
                return (List<T>)cached;

            var modulePath = GetModulePathFromRelativePath(relativePath);
            if (!string.IsNullOrEmpty(modulePath) && _isModuleEnabled != null && !_isModuleEnabled(modulePath))
            {
                var empty = new List<T>();
                _loadCache[cacheKey] = empty;
                return empty;
            }

            var basePath = Path.Combine(StoragePathHelper.GetStorageRoot(), "Modules", relativePath);
            var items = new List<T>();

            if (!Directory.Exists(basePath))
            {
                _loadCache[cacheKey] = items;
                return items;
            }

            foreach (var file in Directory.GetFiles(basePath, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var _fn = Path.GetFileName(file);
                    if (string.Equals(_fn, "categories.json", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(_fn, "built_in_categories.json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);

                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var list = JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
                    if (list != null)
                    {
                        var filtered = new List<T>();
                        foreach (var item in list)
                        {
                            if (item == null)
                                continue;

                            if (item is TM.Framework.Common.Models.IEnableable enableable && !enableable.IsEnabled)
                                continue;

                            filtered.Add(item);
                        }

                        items.AddRange(filtered);
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GuideIndexBuilder] 读取文件失败 [{file}]: {ex.Message}");
                }
            }

            _loadCache[cacheKey] = items;
            return items;
        }

        private static string GetModulePathFromRelativePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return string.Empty;

            var parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return string.Empty;

            return $"{parts[0]}/{parts[1]}";
        }

        #endregion
    }
}

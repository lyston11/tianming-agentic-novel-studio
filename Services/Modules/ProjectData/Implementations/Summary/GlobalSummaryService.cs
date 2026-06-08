using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Index;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class GlobalSummaryService
    {
        private GlobalSummary? _cachedSummary;
        private DateTime _cacheTime = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public GlobalSummaryService()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        public async Task<GlobalSummary> GetGlobalSummaryAsync()
        {
            if (_cachedSummary != null && DateTime.UtcNow - _cacheTime < _cacheExpiry)
            {
                return _cachedSummary;
            }

            var computed = await ComputeRealTimeAsync().ConfigureAwait(false);
            _cachedSummary = computed;
            _cacheTime = DateTime.UtcNow;
            return computed;
        }

        public async Task<GlobalSummary> ComputeRealTimeAsync()
        {
            var summary = new GlobalSummary();

            try
            {
                var _plotPath = Path.Combine(StoragePathHelper.GetStorageRoot(), "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");
                var _charPath = Path.Combine(StoragePathHelper.GetStorageRoot(), "Modules", "Design", "Elements", "CharacterRules", "character_rules.json");
                List<PlotRulesData>? _sharedPlot = null;
                List<CharacterRulesData>? _sharedChar = null;
                await Task.WhenAll(
                    File.Exists(_plotPath) ? Task.Run(async () => { await using var s = File.OpenRead(_plotPath); _sharedPlot = await JsonSerializer.DeserializeAsync<List<PlotRulesData>>(s, JsonOptions).ConfigureAwait(false); }) : Task.CompletedTask,
                    File.Exists(_charPath) ? Task.Run(async () => { await using var s = File.OpenRead(_charPath); _sharedChar = await JsonSerializer.DeserializeAsync<List<CharacterRulesData>>(s, JsonOptions).ConfigureAwait(false); }) : Task.CompletedTask
                ).ConfigureAwait(false);

                await Task.WhenAll(
                    ExtractCoreRulesAsync(summary),
                    ExtractCoreFactionsAsync(summary),
                    ExtractStorySummaryAsync(summary, _sharedPlot),
                    ExtractMainConflictAsync(summary, _sharedPlot),
                    ExtractUsedElementsAsync(summary, _sharedPlot, _sharedChar),
                    ExtractProgressInfoAsync(summary)).ConfigureAwait(false);

                TM.App.Log("[GlobalSummaryService] 实时计算完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 实时计算失败: {ex.Message}");
            }

            return summary;
        }

        public void InvalidateCache()
        {
            _cachedSummary = null;
            _cacheTime = DateTime.MinValue;
            TM.App.Log("[GlobalSummaryService] 缓存已清除");
        }

        private async Task ExtractCoreFactionsAsync(GlobalSummary summary)
        {
            var factionsPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "FactionRules", "faction_rules.json");

            if (!File.Exists(factionsPath)) return;

            try
            {
                await using var factionsStream = File.OpenRead(factionsPath);
                var factions = await JsonSerializer.DeserializeAsync<List<Dictionary<string, JsonElement>>>(factionsStream, JsonOptions).ConfigureAwait(false);

                if (factions == null) return;

                summary.CoreFactions = factions
                    .Where(f =>
                    {
                        if (!f.TryGetValue("IsEnabled", out var enabledEl))
                            return true;
                        return enabledEl.ValueKind != JsonValueKind.False;
                    })
                    .Select(f =>
                    {
                        var name = GetJsonString(f, "Name");
                        var type = GetJsonString(f, "FactionType");
                        var goal = GetJsonString(f, "Goal");
                        var leader = GetJsonString(f, "Leader");
                        var brief = string.IsNullOrWhiteSpace(type) ? name : $"{name}({type})";
                        var deep = string.Join("；", new[] { goal, leader }.Where(s => !string.IsNullOrWhiteSpace(s)));

                        return new IndexItem
                        {
                            Id = GetJsonString(f, "Id"),
                            Name = name,
                            Type = type,
                            BriefSummary = brief,
                            DeepSummary = TruncateString(deep, 80)
                        };
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取势力失败: {ex.Message}");
            }
        }

        private async Task ExtractStorySummaryAsync(GlobalSummary summary, List<PlotRulesData>? plotRules = null)
        {
            var outlinePath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Generate", "GlobalSettings", "Outline", "outline_data.json");

            if (File.Exists(outlinePath))
            {
                try
                {
                    await using var outlineStream = File.OpenRead(outlinePath);
                    var outlines = await JsonSerializer.DeserializeAsync<List<Models.Generate.StrategicOutline.OutlineData>>(outlineStream, JsonOptions).ConfigureAwait(false);
                    var outline = outlines
                        ?.Where(o => o.IsEnabled)
                        .OrderByDescending(o => o.UpdatedAt)
                        .FirstOrDefault();

                    var text = outline?.OneLineOutline;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        summary.StorySummary = TruncateString(text, 100);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GlobalSummaryService] 提取主题失败: {ex.Message}");
                }
            }

            try
            {
                List<PlotRulesData>? rules = plotRules;
                if (rules == null)
                {
                    var plotRulesPath = Path.Combine(
                        StoragePathHelper.GetStorageRoot(),
                        "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");
                    if (!File.Exists(plotRulesPath)) return;
                    await using var plotRulesStream = File.OpenRead(plotRulesPath);
                    rules = await JsonSerializer.DeserializeAsync<List<PlotRulesData>>(plotRulesStream, JsonOptions).ConfigureAwait(false);
                }
                var item = rules
                    ?.Where(p => p.IsEnabled)
                    .OrderByDescending(p => p.GetImportanceWeight())
                    .ThenByDescending(p => p.UpdatedAt)
                    .FirstOrDefault();

                if (item == null) return;

                var brief = item.OneLineSummary;
                if (string.IsNullOrWhiteSpace(brief))
                    brief = item.Goal;
                if (string.IsNullOrWhiteSpace(brief))
                    brief = item.Conflict;

                if (!string.IsNullOrWhiteSpace(brief))
                {
                    summary.StorySummary = $"{item.Name}：{TruncateString(brief, 100)}";
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取主题失败: {ex.Message}");
            }
        }

        #region 提取方法

        private async Task ExtractCoreRulesAsync(GlobalSummary summary)
        {
            var rulesPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "GlobalSettings", "WorldRules", "world_rules.json");

            if (!File.Exists(rulesPath)) return;

            try
            {
                await using var rulesStream = File.OpenRead(rulesPath);
                var rules = await JsonSerializer.DeserializeAsync<List<Models.Design.Worldview.WorldRulesData>>(rulesStream, JsonOptions).ConfigureAwait(false);

                if (rules == null) return;

                summary.CoreRules = rules
                    .Where(r => r.IsEnabled)
                    .OrderByDescending(r => r.GetImportanceWeight())
                    .Select(r => new IndexItem
                    {
                        Id = r.Id,
                        Name = r.Name,
                        Type = r.Category,
                        BriefSummary = $"{r.Name}({r.Category})",
                        DeepSummary = TruncateString(r.GetCoreSummary(), 80)
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取规则失败: {ex.Message}");
            }
        }

        private async Task ExtractMainConflictAsync(GlobalSummary summary, List<PlotRulesData>? plotRules = null)
        {
            try
            {
                List<PlotRulesData>? rules = plotRules;
                if (rules == null)
                {
                    var plotRulesPath = Path.Combine(
                        StoragePathHelper.GetStorageRoot(),
                        "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");
                    if (!File.Exists(plotRulesPath)) return;
                    await using var plotRulesStream = File.OpenRead(plotRulesPath);
                    rules = await JsonSerializer.DeserializeAsync<List<PlotRulesData>>(plotRulesStream, JsonOptions).ConfigureAwait(false);
                }

                var conflict = rules
                    ?.Where(p => p.IsEnabled)
                    .Where(p => !string.IsNullOrWhiteSpace(p.Conflict))
                    .OrderByDescending(p => p.GetImportanceWeight())
                    .ThenByDescending(p => p.UpdatedAt)
                    .FirstOrDefault();

                if (conflict != null)
                {
                    summary.MainConflict = $"{conflict.Name}：{TruncateString(conflict.Conflict, 80)}";
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取主线冲突失败: {ex.Message}");
            }
        }

        private async Task ExtractUsedElementsAsync(GlobalSummary summary, List<PlotRulesData>? plotRules = null, List<CharacterRulesData>? charRules = null)
        {
            try
            {
                var rules = plotRules;
                if (rules == null)
                {
                    var plotRulesPath = Path.Combine(
                        StoragePathHelper.GetStorageRoot(),
                        "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");
                    if (File.Exists(plotRulesPath))
                    {
                        await using var plotRulesStream = File.OpenRead(plotRulesPath);
                        rules = await JsonSerializer.DeserializeAsync<List<PlotRulesData>>(plotRulesStream, JsonOptions).ConfigureAwait(false) ?? new();
                    }
                }
                if (rules != null)
                    summary.UsedElements.UsedPlotPatterns = rules
                        .Where(p => p.IsEnabled)
                        .Select(p => p.EventType)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct()
                        .Take(20)
                        .ToList();

                var chars = charRules;
                if (chars == null)
                {
                    var characterRulesPath = Path.Combine(
                        StoragePathHelper.GetStorageRoot(),
                        "Modules", "Design", "Elements", "CharacterRules", "character_rules.json");
                    if (File.Exists(characterRulesPath))
                    {
                        await using var characterRulesStream = File.OpenRead(characterRulesPath);
                        chars = await JsonSerializer.DeserializeAsync<List<CharacterRulesData>>(characterRulesStream, JsonOptions).ConfigureAwait(false) ?? new();
                    }
                }
                if (chars != null)
                    summary.UsedElements.UsedAbilities = chars
                        .Where(c => c.IsEnabled)
                        .Select(c => c.SpecialAbilities)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => TruncateString(s, 20))
                        .Distinct()
                        .Take(20)
                        .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取已使用元素失败: {ex.Message}");
            }
        }

        private async Task ExtractProgressInfoAsync(GlobalSummary summary)
        {
            try
            {
                var blueprintPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Generate", "Elements", "Blueprint", "blueprint_data.json");

                if (File.Exists(blueprintPath))
                {
                    await using var blueprintStream = File.OpenRead(blueprintPath);
                    var blueprints = await JsonSerializer.DeserializeAsync<List<Dictionary<string, JsonElement>>>(blueprintStream, JsonOptions).ConfigureAwait(false);

                    if (blueprints != null)
                    {
                        summary.Progress.TotalChapters = blueprints.Select(b => GetJsonString(b, "ChapterId")).Distinct().Count();
                        summary.Progress.CompletedChapters = blueprints
                            .Count(b => !string.IsNullOrEmpty(GetJsonString(b, "OneLineStructure")));
                    }
                }

                summary.Progress.CurrentPhase = "Design";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取进度信息失败: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        private static string GetJsonString(Dictionary<string, JsonElement> dict, string key)
        {
            if (dict.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;
            return string.Empty;
        }

        private static string TruncateString(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length > maxLength ? text[..maxLength] + "..." : text;
        }

        #endregion
    }
}

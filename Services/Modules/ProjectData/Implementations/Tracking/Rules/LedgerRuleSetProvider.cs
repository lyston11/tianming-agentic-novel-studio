using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Services.Modules.ProjectData.Implementations.Tracking.Rules
{
    public sealed class LedgerRuleSetProvider
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private string? _cachedGenre;
        private LedgerRuleSet? _cachedRuleSet;
        private DateTime _cachedFileLastWrite;

        public async Task<LedgerRuleSet> GetRuleSetForGateAsync()
        {
            try
            {
                var genre = await TryResolveGenreFromEnabledCreativeMaterialsAsync().ConfigureAwait(false);
                if (_cachedRuleSet != null && string.Equals(genre, _cachedGenre, StringComparison.Ordinal))
                    return _cachedRuleSet;

                var ruleSet = BuildRuleSetByGenre(genre);
                _cachedGenre = genre;
                _cachedRuleSet = ruleSet;
                return ruleSet;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LedgerRuleSetProvider] 获取规则集失败，回退通用规则: {ex.Message}");
                return LedgerRuleSet.CreateUniversalDefault();
            }
        }

        private async Task<string?> TryResolveGenreFromEnabledCreativeMaterialsAsync()
        {
            var path = StoragePathHelper.GetFilePath(
                "Modules",
                "Design/Templates/CreativeMaterials",
                "creative_materials.json");

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(path);
                if (_cachedRuleSet != null && lastWrite == _cachedFileLastWrite)
                    return _cachedGenre;

                _cachedFileLastWrite = lastWrite;
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var items = JsonSerializer.Deserialize<List<CreativeMaterialData>>(json, JsonOptions) ?? new();

                var enabled = items
                    .Where(i => i != null && i.IsEnabled)
                    .OrderByDescending(i => i!.ModifiedTime)
                    .FirstOrDefault();

                var genre = enabled?.Genre;
                return string.IsNullOrWhiteSpace(genre) ? null : genre.Trim();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LedgerRuleSetProvider] 读取创作模板题材失败，回退通用规则: {ex.Message}");
                return null;
            }
        }

        private static LedgerRuleSet BuildRuleSetByGenre(string? genre)
        {
            if (string.IsNullOrWhiteSpace(genre))
            {
                return LedgerRuleSet.CreateUniversalDefault();
            }

            var g = genre.Trim();

            if (ContainsAny(g, "玄幻", "奇幻", "仙侠", "武侠"))
            {
                var ruleSet = LedgerRuleSet.CreateUniversalDefault();
                ruleSet.EnableAbilityLossRequiresEvent = true;
                ruleSet.AbilityLossKeywords = new List<string>
                {
                    "失去", "丧失", "消散", "失效",
                    "封印", "封禁", "封锁",
                    "剥夺", "废除", "废功", "自废", "散功",
                    "退化", "降级", "削弱", "削减",
                    "代价", "反噬", "牺牲", "透支", "燃烧", "燃血", "燃魂",
                    "重创", "重伤", "受创", "道伤", "损伤", "损耗",
                    "走火入魔", "心魔", "夺走", "抽离", "抽取"
                };
                return ruleSet;
            }

            if (ContainsAny(g, "都市", "现实"))
            {
                var ruleSet = LedgerRuleSet.CreateUniversalDefault();
                ruleSet.EnableAbilityLossRequiresEvent = true;
                ruleSet.AbilityLossKeywords = new List<string>
                {
                    "失去", "丧失", "失效", "作废", "禁用",
                    "封号", "封禁", "冻结",
                    "停职", "撤职", "革职", "解雇", "辞退",
                    "权限回收", "取消资格", "剥夺", "降级",
                    "注销", "吊销", "取缔", "罢免",
                    "解约", "解聘", "解除", "终止",
                    "黑名单", "出局", "代价", "牺牲"
                };
                return ruleSet;
            }

            return LedgerRuleSet.CreateUniversalDefault();
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
            {
                return false;
            }

            foreach (var k in keywords)
            {
                if (string.IsNullOrWhiteSpace(k))
                {
                    continue;
                }

                if (text.Contains(k, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

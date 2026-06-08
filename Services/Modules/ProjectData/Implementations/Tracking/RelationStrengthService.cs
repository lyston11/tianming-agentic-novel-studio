using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Framework.Common.Helpers.Id;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class RelationStrengthService
    {
        private RelationStrengthIndex? _cachedIndex;
        private volatile bool _indexLoaded = false;
        private readonly SemaphoreSlim _indexLoadLock = new(1, 1);
        private readonly SemaphoreSlim _cacheLoadLock = new(1, 1);
        private int _epoch;

        private List<CharacterRulesData>? _relationshipsCache;
        private Dictionary<string, ExplicitRelationInfo>? _explicitRelationsCache;
        private List<PlotRulesData>? _plotRulesCache;
        private List<Models.Generate.ChapterBlueprint.BlueprintData>? _blueprintsCache;
        private DateTime _cacheExpiry = DateTime.MinValue;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(30);

        public RelationStrengthService()
        {
            try
            {
                TM.Framework.Common.Helpers.Storage.StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[RelationStrengthService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[RelationStrengthService] {key}: {ex.Message}");
        }

        public async Task<RelationStrength> GetStrengthAsync(string id1, string id2)
        {
            if (await TryLoadIndexAsync().ConfigureAwait(false))
            {
                var index = _cachedIndex;
                if (index != null)
                {
                    return index.GetStrength(id1, id2);
                }
            }

            return await ComputeStrengthRealtimeAsync(id1, id2).ConfigureAwait(false);
        }

        private async Task<bool> TryLoadIndexAsync()
        {
            if (_indexLoaded)
                return _cachedIndex != null;

            await _indexLoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_indexLoaded)
                    return _cachedIndex != null;

                var epoch = Volatile.Read(ref _epoch);

                var indexPath = Path.Combine(
                    StoragePathHelper.GetProjectConfigPath(),
                    "guides",
                    "relation_strength_index.json");

                if (File.Exists(indexPath))
                {
                    try
                    {
                        await using var stream = File.OpenRead(indexPath);
                        var index = await JsonSerializer.DeserializeAsync<RelationStrengthIndex>(stream, JsonOptions).ConfigureAwait(false);

                        if (epoch != Volatile.Read(ref _epoch))
                            return false;

                        _cachedIndex = index;
                        _indexLoaded = true;
                        return _cachedIndex != null;
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[RelationStrengthService] 加载索引失败: {ex.Message}");
                    }
                }

                if (epoch != Volatile.Read(ref _epoch))
                    return false;

                _indexLoaded = true;
                return false;
            }
            finally
            {
                _indexLoadLock.Release();
            }
        }

        private async Task<RelationStrength> ComputeStrengthRealtimeAsync(string id1, string id2)
        {
            await EnsureCacheLoadedAsync().ConfigureAwait(false);

            RelationStrength? explicitStrength = null;
            if (_explicitRelationsCache != null)
            {
                var key = GetPairKey(id1, id2);
                if (_explicitRelationsCache.TryGetValue(key, out var info))
                {
                    var baseStrength = DetermineStrengthByRelationType(info.RelationType);
                    var finalStrength = ApplyStrengthHint(baseStrength, info.StrengthHint);
                    if (finalStrength == RelationStrength.Strong)
                        return RelationStrength.Strong;

                    explicitStrength = finalStrength;
                }
            }

            if (_plotRulesCache?.Any(c =>
            {
                var participants = GetParticipants(c);
                return participants.Contains(id1) && participants.Contains(id2);
            }) == true)
                return RelationStrength.Strong;

            if (_blueprintsCache?.Any(b =>
                ExtractIdsFromText(b.Cast, CharacterRulesDataSource).Contains(id1)
                && ExtractIdsFromText(b.Cast, CharacterRulesDataSource).Contains(id2)) == true)
                return RelationStrength.Medium;

            if (explicitStrength.HasValue && explicitStrength.Value > RelationStrength.Weak)
                return explicitStrength.Value;

            return RelationStrength.Weak;
        }

        private async Task EnsureCacheLoadedAsync()
        {
            if (_cacheExpiry > DateTime.UtcNow)
                return;

            await _cacheLoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cacheExpiry > DateTime.UtcNow)
                    return;

                var epoch = Volatile.Read(ref _epoch);

                var plotRulesTask = LoadPlotRulesAsync();
                var blueprintsTask = LoadBlueprintsAsync();
                var relationshipsTask = LoadCharacterRelationshipsAsync();

                await Task.WhenAll(plotRulesTask, blueprintsTask, relationshipsTask).ConfigureAwait(false);

                if (epoch != Volatile.Read(ref _epoch))
                    return;

                var relationships = await relationshipsTask.ConfigureAwait(false);
                _plotRulesCache = await plotRulesTask.ConfigureAwait(false);
                _blueprintsCache = await blueprintsTask.ConfigureAwait(false);
                _relationshipsCache = relationships;
                _explicitRelationsCache = BuildExplicitRelations(_relationshipsCache);
                _characterRulesCache = _relationshipsCache;
                _cacheExpiry = DateTime.UtcNow.Add(_cacheDuration);

                TM.App.Log($"[RelationStrengthService] 缓存已加载，有效期{_cacheDuration.TotalSeconds}秒");
            }
            finally
            {
                _cacheLoadLock.Release();
            }
        }

        public void InvalidateCache()
        {
            Interlocked.Increment(ref _epoch);
            _cachedIndex = null;
            _indexLoaded = false;
            _cacheExpiry = DateTime.MinValue;
            _plotRulesCache = null;
            _blueprintsCache = null;
            _relationshipsCache = null;
            _explicitRelationsCache = null;
            _characterRulesCache = null;
            TM.App.Log("[RelationStrengthService] 缓存已清除");
        }

        public async Task<RelationStrengthIndex> BuildStrengthIndexAsync()
        {
            var index = new RelationStrengthIndex();

            try
            {
                var plotRulesTask = LoadPlotRulesAsync();
                var blueprintsTask = LoadBlueprintsAsync();
                await Task.WhenAll(plotRulesTask, blueprintsTask).ConfigureAwait(false);
                var plotRules = await plotRulesTask.ConfigureAwait(false);
                var blueprints = await blueprintsTask.ConfigureAwait(false);

                foreach (var plotRule in plotRules)
                {
                    var participants = GetParticipants(plotRule);

                    for (int i = 0; i < participants.Count; i++)
                    {
                        for (int j = i + 1; j < participants.Count; j++)
                        {
                            index.UpgradeStrength(participants[i], participants[j], RelationStrength.Strong);
                        }
                    }
                }

                foreach (var blueprint in blueprints)
                {
                    var characters = ExtractIdsFromText(blueprint.Cast, CharacterRulesDataSource);

                    for (int i = 0; i < characters.Count; i++)
                    {
                        for (int j = i + 1; j < characters.Count; j++)
                        {
                            index.EnsureMinStrength(characters[i], characters[j], RelationStrength.Medium);
                        }
                    }
                }

                TM.App.Log($"[RelationStrengthService] 索引构建完成，共{index.Pairs.Count}条关联");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[RelationStrengthService] 构建索引失败: {ex.Message}");
            }

            return index;
        }

        private List<CharacterRulesData>? _characterRulesCache;
        private List<CharacterRulesData> CharacterRulesDataSource => _characterRulesCache ??= new List<CharacterRulesData>();

        private static List<string> SplitNames(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.Split(new[] { ',', '，', '、', ';', '；', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> ExtractIdsFromText(string? castText, List<CharacterRulesData> characters)
        {
            if (string.IsNullOrWhiteSpace(castText) || characters == null || characters.Count == 0)
                return new List<string>();

            var nameToId = characters
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

            return SplitNames(castText)
                .Select(n => nameToId.TryGetValue(n, out var id) ? id : null)
                .Where(id => id != null)
                .Distinct()
                .ToList()!;
        }

        private RelationStrength DetermineStrengthByRelationType(string relationType)
        {
            return relationType switch
            {
                "仇敌" or "师徒" or "恋人" or "血亲" or "宿敌" => RelationStrength.Strong,
                "同门" or "战友" or "同阵营" or "朋友" => RelationStrength.Medium,
                _ => RelationStrength.Weak
            };
        }

        private async Task<List<PlotRulesData>> LoadPlotRulesAsync()
        {
            var path = Path.Combine(StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");
            if (!File.Exists(path))
                return new List<PlotRulesData>();
            try
            {
                await using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<List<PlotRulesData>>(stream, JsonOptions).ConfigureAwait(false) ?? new List<PlotRulesData>();
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(LoadPlotRulesAsync), ex);
                return new List<PlotRulesData>();
            }
        }

        private async Task<List<Models.Generate.ChapterBlueprint.BlueprintData>> LoadBlueprintsAsync()
        {
            var path = Path.Combine(StoragePathHelper.GetStorageRoot(),
                "Modules", "Generate", "ChapterBlueprint", "Blueprint");
            return await LoadItemsFromDirectoryAsync<Models.Generate.ChapterBlueprint.BlueprintData>(path).ConfigureAwait(false);
        }

        private async Task<List<T>> LoadItemsFromDirectoryAsync<T>(string directoryPath)
        {
            var items = new List<T>();
            if (!Directory.Exists(directoryPath))
                return items;

            foreach (var file in Directory.GetFiles(directoryPath, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    await using var stream = File.OpenRead(file);
                    var item = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions).ConfigureAwait(false);
                    if (item != null)
                        items.Add(item);
                }
                catch (Exception ex)
                {
                    DebugLogOnce(nameof(LoadItemsFromDirectoryAsync), ex);
                }
            }

            return items;
        }

        private static List<string> GetParticipants(PlotRulesData plotRule)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(plotRule.MainCharacters))
                return ids;

            var parts = plotRule.MainCharacters
                .Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var trimmed = p.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    ids.Add(trimmed);
            }

            return ids;
        }

        private class ExplicitRelationInfo
        {
            public string RelationType { get; set; } = string.Empty;
            public string? StrengthHint { get; set; }
        }

        private static string GetPairKey(string id1, string id2)
        {
            return string.Compare(id1, id2, StringComparison.Ordinal) < 0 ? $"{id1}_{id2}" : $"{id2}_{id1}";
        }

        private RelationStrength ApplyStrengthHint(RelationStrength baseStrength, string? strengthHint)
        {
            if (string.IsNullOrWhiteSpace(strengthHint))
                return baseStrength;

            return strengthHint switch
            {
                "Strong" => RelationStrength.Strong,
                "Medium" when baseStrength < RelationStrength.Medium => RelationStrength.Medium,
                _ => baseStrength
            };
        }

        private async Task<List<CharacterRulesData>> LoadCharacterRelationshipsAsync()
        {
            var result = new List<CharacterRulesData>();
            var path = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "CharacterRules", "character_rules.json");

            if (!File.Exists(path))
                return result;

            try
            {
                await using var stream = File.OpenRead(path);
                var data = await JsonSerializer.DeserializeAsync<List<CharacterRulesData>>(stream, JsonOptions).ConfigureAwait(false);
                if (data != null)
                    result.AddRange(data);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[RelationStrengthService] 加载角色规则失败: {ex.Message}");
            }

            return result;
        }

        private Dictionary<string, ExplicitRelationInfo> BuildExplicitRelations(List<CharacterRulesData>? characterRules)
        {
            var result = new Dictionary<string, ExplicitRelationInfo>();
            if (characterRules == null)
                return result;

            foreach (var data in characterRules)
            {
                if (string.IsNullOrWhiteSpace(data.Id) || string.IsNullOrWhiteSpace(data.TargetCharacterName))
                    continue;

                var baseStrength = DetermineStrengthByRelationType(data.RelationshipType ?? string.Empty);
                if (baseStrength <= RelationStrength.Weak)
                    continue;

                var targetId = data.TargetCharacterName;
                if (!ShortIdGenerator.IsLikelyId(targetId))
                    continue;

                var key = GetPairKey(data.Id, targetId);

                if (result.TryGetValue(key, out var existing))
                {
                    var existingStrength = DetermineStrengthByRelationType(existing.RelationType);
                    if (existingStrength >= baseStrength)
                        continue;
                }

                result[key] = new ExplicitRelationInfo
                {
                    RelationType = data.RelationshipType ?? string.Empty,
                    StrengthHint = null
                };
            }

            return result;
        }
    }

    public class RelationStrengthIndex
    {
        [System.Text.Json.Serialization.JsonPropertyName("Pairs")] public Dictionary<string, RelationStrength> Pairs { get; set; } = new();

        public void AddPair(string id1, string id2, RelationStrength strength)
        {
            var key = GetKey(id1, id2);
            Pairs[key] = strength;
        }

        public void UpgradeStrength(string id1, string id2, RelationStrength newStrength)
        {
            var key = GetKey(id1, id2);
            if (!Pairs.TryGetValue(key, out var cur) || cur < newStrength)
                Pairs[key] = newStrength;
        }

        public void EnsureMinStrength(string id1, string id2, RelationStrength minStrength)
        {
            var key = GetKey(id1, id2);
            if (!Pairs.ContainsKey(key))
                Pairs[key] = minStrength;
        }

        public RelationStrength GetStrength(string id1, string id2)
        {
            var key = GetKey(id1, id2);
            return Pairs.GetValueOrDefault(key, RelationStrength.Weak);
        }

        private string GetKey(string id1, string id2)
        {
            return string.Compare(id1, id2) < 0 ? $"{id1}_{id2}" : $"{id2}_{id1}";
        }
    }
}

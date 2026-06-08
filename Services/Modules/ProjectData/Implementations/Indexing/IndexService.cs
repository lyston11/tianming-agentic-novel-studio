using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Index;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class IndexService
    {
        private const int UpstreamIndexMaxItemsPerLayer = 200;

        private static readonly Dictionary<string, string[]> LayerDependencies = new()
        {
            ["SmartParsing"] = Array.Empty<string>(),
            ["Templates"] = new[] { "SmartParsing" },
            ["Worldview"] = new[] { "Templates" },
            ["Characters"] = new[] { "Templates", "Worldview" },
            ["Factions"] = new[] { "Templates", "Worldview", "Characters" },
            ["Locations"] = new[] { "Templates", "Worldview", "Characters", "Factions" },
            ["Plot"] = new[] { "Templates", "Worldview", "Characters", "Factions", "Locations" },

            ["Outline"] = new[] { "Worldview", "Characters", "Factions", "Locations", "Plot" },
            ["Planning"] = new[] { "Worldview", "Characters", "Factions", "Locations", "Plot", "Outline" },
            ["Blueprint"] = new[] { "Worldview", "Characters", "Factions", "Locations", "Plot", "Outline", "Planning" },
            ["Content"] = new[] { "Worldview", "Characters", "Factions", "Locations", "Plot", "Outline", "Planning", "Blueprint" }
        };

        public async Task<UpstreamIndex> BuildUpstreamIndexAsync(string targetLayer)
        {
            var index = new UpstreamIndex();

            if (!LayerDependencies.TryGetValue(targetLayer, out var dependencies))
                return index;

            if (InfoLogDedup.ShouldLog($"IndexService:BuildUpstream:{targetLayer}"))
                TM.App.Log($"[IndexService] 构建上游索引: targetLayer={targetLayer}");

            var layerTasks = dependencies.Select(async dep =>
            {
                var items = await LoadLayerItemsAsync(dep, UpstreamIndexMaxItemsPerLayer).ConfigureAwait(false);
                var indexItems = items.Select(i => BuildIndexItem(i, useDeepSummary: false)).ToList();
                return (dep, indexItems);
            });
            var layerResults = await Task.WhenAll(layerTasks).ConfigureAwait(false);
            foreach (var (dep, indexItems) in layerResults)
            {
                SetIndexProperty(index, dep, indexItems);
                if (InfoLogDedup.ShouldLog($"IndexService:Layer:{dep}"))
                    TM.App.Log($"[IndexService] 层级 {dep} 加载了 {indexItems.Count} 个实体");
            }

            return index;
        }

        public IndexItem BuildIndexItem<T>(T item, bool useDeepSummary = false) where T : IIndexable
        {
            return new IndexItem
            {
                Id = item.Id,
                Name = item.Name,
                Type = item.GetItemType(),
                BriefSummary = item.GetBriefSummary(),
                DeepSummary = useDeepSummary ? item.GetDeepSummary() : string.Empty
            };
        }

        public async Task<List<T>> LoadByIdsAsync<T>(List<string> ids) where T : class
        {
            var result = new List<T>();
            if (ids == null || ids.Count == 0)
                return result;

            var idSet = new HashSet<string>(ids);

            foreach (var layer in LayerStoragePaths.Keys)
            {
                var items = await LoadLayerItemsAsync(layer).ConfigureAwait(false);
                foreach (var item in items)
                {
                    if (idSet.Contains(item.Id))
                    {
                        if (typeof(IIndexable).IsAssignableFrom(typeof(T)))
                        {
                            result.Add((T)(object)item);
                        }
                        else if (typeof(T) == typeof(IndexItem))
                        {
                            result.Add((T)(object)BuildIndexItem(item, useDeepSummary: true));
                        }
                        else if (typeof(T) == typeof(object))
                        {
                            result.Add((T)(object)BuildIndexItem(item, useDeepSummary: true));
                        }
                        idSet.Remove(item.Id);
                        if (idSet.Count == 0) break;
                    }
                }
                if (idSet.Count == 0) break;
            }

            return result;
        }

        public async Task<List<IndexItem>> LoadIndexItemsByIdsAsync(List<string> ids, bool useDeepSummary = true)
        {
            var result = new List<IndexItem>();
            if (ids == null || ids.Count == 0)
                return result;

            var idSet = new HashSet<string>(ids);

            foreach (var layer in LayerStoragePaths.Keys)
            {
                var items = await LoadLayerItemsAsync(layer).ConfigureAwait(false);
                foreach (var item in items)
                {
                    if (idSet.Contains(item.Id))
                    {
                        result.Add(BuildIndexItem(item, useDeepSummary));
                        idSet.Remove(item.Id);
                        if (idSet.Count == 0) break;
                    }
                }
                if (idSet.Count == 0) break;
            }

            return result;
        }

        public async Task<List<IndexItem>> GetRelatedEntitiesAsync(
            string focusId,
            RelationStrengthService relationService,
            int maxStrong = 5,
            int maxMedium = 10)
        {
            var result = new List<IndexItem>();

            var allItems = await LoadLayerItemsAsync("Characters").ConfigureAwait(false);

            foreach (var item in allItems)
            {
                if (item.Id == focusId) continue;

                var strength = await relationService.GetStrengthAsync(focusId, item.Id).ConfigureAwait(false);

                if (strength == Models.Context.RelationStrength.Strong && result.Count(r => r.RelationStrength == "Strong") < maxStrong)
                {
                    var indexItem = BuildIndexItem(item, useDeepSummary: true);
                    indexItem.RelationStrength = "Strong";
                    result.Add(indexItem);
                }
                else if (strength == Models.Context.RelationStrength.Medium && result.Count(r => r.RelationStrength == "Medium") < maxMedium)
                {
                    var indexItem = BuildIndexItem(item, useDeepSummary: false);
                    indexItem.RelationStrength = "Medium";
                    result.Add(indexItem);
                }
            }

            return result;
        }

        public async Task<IndexItem?> GetIndexItemAsync(string id, string layer)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            var items = await LoadLayerItemsAsync(layer).ConfigureAwait(false);
            var item = items.FirstOrDefault(i => i.Id == id);

            if (item != null)
            {
                return BuildIndexItem(item, useDeepSummary: true);
            }

            return null;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly Dictionary<string, string[]> LayerStoragePaths = new()
        {
            ["SmartParsing"] = new[] { "Design/SmartParsing/BookAnalysis" },
            ["Templates"] = new[] { "Design/Templates/CreativeMaterials" },
            ["Worldview"] = new[] { "Design/GlobalSettings/WorldRules" },
            ["Characters"] = new[] { "Design/Elements/CharacterRules" },
            ["Factions"] = new[] { "Design/Elements/FactionRules" },
            ["Locations"] = new[] { "Design/Elements/LocationRules" },
            ["Plot"] = new[] { "Design/Elements/PlotRules" },

            ["Outline"] = new[] { "Generate/GlobalSettings/Outline" },
            ["Planning"] = new[] { "Generate/Elements/Chapter" },
            ["Blueprint"] = new[] { "Generate/Elements/Blueprint" }
        };

        private Task<List<IIndexable>> LoadLayerItemsAsync(string layer)
            => LoadLayerItemsAsync(layer, int.MaxValue);

        private async Task<List<IIndexable>> LoadLayerItemsAsync(string layer, int maxItems)
        {
            var items = new List<IIndexable>();

            if (maxItems <= 0)
            {
                return items;
            }

            if (!LayerStoragePaths.TryGetValue(layer, out var paths))
                return items;

            var storageRoot = StoragePathHelper.GetStorageRoot();

            bool reachedLimit = false;

            foreach (var subPath in paths)
            {
                var dirPath = Path.Combine(storageRoot, "Modules", subPath);
                if (!Directory.Exists(dirPath))
                    continue;

                foreach (var file in Directory.GetFiles(dirPath, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var _fn2 = Path.GetFileName(file);
                        if (string.Equals(_fn2, "categories.json", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_fn2, "built_in_categories.json", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var fileBaseName = Path.GetFileName(file);

                        var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);

                        if (doc.RootElement.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            if (element.ValueKind != JsonValueKind.Object)
                                continue;

                            var data = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                            foreach (var prop in element.EnumerateObject())
                                data[prop.Name] = prop.Value;

                            data["__file"] = JsonDocument.Parse($"\"{fileBaseName}\"").RootElement.Clone();

                            var indexable = ConvertToIndexable(data, layer);
                            if (indexable != null)
                            {
                                items.Add(indexable);
                                if (items.Count >= maxItems)
                                {
                                    reachedLimit = true;
                                    break;
                                }
                            }
                        }

                        if (reachedLimit)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[IndexService] 加载{file}失败: {ex.Message}");
                    }
                }

                if (reachedLimit)
                {
                    break;
                }
            }

            return items;
        }

        private IIndexable? ConvertToIndexable(Dictionary<string, JsonElement> data, string layer)
        {
            var id = GetJsonString(data, "Id");
            var name = GetJsonString(data, "Name");

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                return null;

            var itemType = layer switch
            {
                "Characters" => GetJsonString(data, "CharacterType") ?? "角色",
                "Worldview" => BuildWorldviewItemType(data),
                "Factions" => GetJsonString(data, "FactionType") ?? "势力",
                "Locations" => GetJsonString(data, "LocationType") ?? "位置",
                "Plot" => GetJsonString(data, "EventType")
                          ?? GetJsonString(data, "StoryPhase")
                          ?? "剧情",
                "SmartParsing" => "拆书",
                "Templates" => "素材",
                "Outline" => "大纲",
                "Planning" => "章节",
                "Blueprint" => "蓝图",
                _ => layer
            };

            var briefSummary = BuildBriefSummary(data, layer, itemType, name);
            var deepSummary = BuildDeepSummary(data, layer, itemType);

            return new SimpleIndexable(id, name, itemType, briefSummary, deepSummary);
        }

        private static string BuildWorldviewItemType(Dictionary<string, JsonElement> data)
        {
            return "世界观规则";
        }

        private string BuildDeepSummary(Dictionary<string, JsonElement> data, string layer, string itemType)
        {
            var parts = new List<string>();

            var fields = layer switch
            {
                "Characters" => new[] { "Identity", "Want", "Need", "FlawBelief", "GrowthPath", "SpecialAbilities" },
                "Worldview" => new[] { "OneLineSummary", "PowerSystem", "HardRules", "SpecialLaws", "Cosmology" },
                "Factions" => new[] { "Goal", "Leader", "MemberTraits", "StrengthTerritory" },
                "Locations" => new[] { "Description", "Terrain", "Climate", "HistoricalSignificance" },
                "Plot" => new[] { "OneLineSummary", "Goal", "Conflict", "MainPlotPush", "CharacterGrowth" },
                "SmartParsing" => new[] { "WorldBuildingMethod", "PowerSystemDesign", "WorldviewHighlights" },
                "Templates" => new[] { "OverallIdea", "WorldviewHighlights", "CharacterHighlights", "PlotHighlights" },
                "Outline" => new[] { "OneLineOutline", "CoreConflict", "Theme", "EndingState", "OutlineOverview" },
                "Planning" => new[] { "MainGoal", "KeyTurn", "Hook", "WorldInfoDrop", "CharacterArcProgress" },
                "Blueprint" => new[] { "OneLineStructure", "SceneTitle", "PovCharacter", "Opening", "Turning", "Ending" },
                _ => Array.Empty<string>()
            };

            foreach (var field in fields)
            {
                var value = GetJsonString(data, field);
                if (!string.IsNullOrEmpty(value))
                {
                    parts.Add(value.Length > 40 ? value[..40] + "..." : value);
                    if (parts.Count >= 2) break;
                }
            }

            return string.Join("。", parts);
        }

        private static string BuildBriefSummary(Dictionary<string, JsonElement> data, string layer, string itemType, string name)
        {
            if (string.Equals(layer, "SmartParsing", StringComparison.OrdinalIgnoreCase)
                && string.Equals(itemType, "拆书", StringComparison.Ordinal))
            {
                var genre = GetJsonString(data, "SourceGenre");
                if (string.IsNullOrEmpty(genre))
                    genre = GetJsonString(data, "Genre");

                var title = GetJsonString(data, "SourceBookTitle");
                if (!string.IsNullOrEmpty(title))
                    return $"{title}({genre})";

                return !string.IsNullOrEmpty(genre) ? $"{name}({genre})" : $"{name}(拆书)";
            }

            return $"{name}({itemType})";
        }

        private static string GetJsonString(Dictionary<string, JsonElement> dict, string key)
        {
            if (dict.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;
            return string.Empty;
        }

        private void SetIndexProperty(UpstreamIndex index, string layer, List<IndexItem> items)
        {
            switch (layer)
            {
                case "SmartParsing":
                    index.SmartParsing = items;
                    break;
                case "Templates":
                    index.Templates = items;
                    break;
                case "Worldview":
                    index.Worldview = items;
                    break;
                case "Characters":
                    index.Characters = items;
                    break;
                case "Factions":
                    index.Factions = items;
                    break;
                case "Plot":
                    index.Plot = items;
                    break;
                case "Locations":
                    index.Locations = items;
                    break;
                case "Outline":
                    index.Outline = items;
                    break;
                case "Planning":
                    index.Planning = items;
                    break;
                case "Blueprint":
                    index.Blueprint = items;
                    break;
                case "Content":
                    break;
            }
        }
    }

    internal class SimpleIndexable : IIndexable
    {
        public string Id { get; }
        public string Name { get; }
        private readonly string _itemType;
        private readonly string _briefSummary;
        private readonly string _deepSummary;

        public SimpleIndexable(string id, string name, string itemType, string briefSummary, string deepSummary)
        {
            Id = id;
            Name = name;
            _itemType = itemType;
            _briefSummary = briefSummary;
            _deepSummary = deepSummary;
        }

        public string GetItemType() => _itemType;
        public string GetBriefSummary() => _briefSummary;
        public string GetDeepSummary() => _deepSummary;
    }
}

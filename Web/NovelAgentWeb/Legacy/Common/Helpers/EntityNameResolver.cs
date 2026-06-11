using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TM.Framework.Common.Helpers
{
    public static class EntityNameResolver
    {
        static EntityNameResolver()
        {
            StoragePathHelper.CurrentProjectChanged += (_, _) =>
            {
                Invalidate();
                PreloadInBackground();
            };
        }

        private static readonly object _lock = new();
        private static readonly System.Threading.SemaphoreSlim _coldLoadSemaphore = new(1, 1);
        private static Dictionary<string, string>? _characterMap;
        private static Dictionary<string, string>? _locationMap;
        private static Dictionary<string, string>? _factionMap;
        private static Dictionary<string, string>? _plotRuleMap;
        private static Dictionary<string, string>? _foreshadowingMap;
        private static Dictionary<string, string>? _conflictMap;
        private static Dictionary<string, string>? _worldRuleMap;
        private static Dictionary<string, string>? _volumeDesignMap;
        private static Dictionary<string, string>? _chapterPlanMap;
        private static Dictionary<string, string>? _blueprintMap;
        private static Dictionary<string, string>? _outlineMap;
        private static Dictionary<string, string>? _itemMap;
        private static Dictionary<string, string>? _secretMap;
        private static Dictionary<string, string>? _pledgeMap;
        private static Dictionary<string, string>? _deadlineMap;
        private static DateTime _lastLoadTime = DateTime.MinValue;
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

        public static string Resolve(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return entityId;

            EnsureLoaded();

            lock (_lock)
            {
                if (_foreshadowingMap?.TryGetValue(entityId, out var fName) == true && !string.IsNullOrEmpty(fName))
                    return fName;
                if (_conflictMap?.TryGetValue(entityId, out var cName) == true && !string.IsNullOrEmpty(cName))
                    return cName;
                if (_characterMap?.TryGetValue(entityId, out var charName) == true && !string.IsNullOrEmpty(charName))
                    return charName;
                if (_locationMap?.TryGetValue(entityId, out var locName) == true && !string.IsNullOrEmpty(locName))
                    return locName;
                if (_factionMap?.TryGetValue(entityId, out var facName) == true && !string.IsNullOrEmpty(facName))
                    return facName;
                if (_plotRuleMap?.TryGetValue(entityId, out var plotName) == true && !string.IsNullOrEmpty(plotName))
                    return plotName;
                if (_worldRuleMap?.TryGetValue(entityId, out var worldName) == true && !string.IsNullOrEmpty(worldName))
                    return worldName;
                if (_volumeDesignMap?.TryGetValue(entityId, out var volDesignName) == true && !string.IsNullOrEmpty(volDesignName))
                    return volDesignName;
                if (_chapterPlanMap?.TryGetValue(entityId, out var chapterName) == true && !string.IsNullOrEmpty(chapterName))
                    return chapterName;
                if (_blueprintMap?.TryGetValue(entityId, out var bpName) == true && !string.IsNullOrEmpty(bpName))
                    return bpName;
                if (_outlineMap?.TryGetValue(entityId, out var outlineName) == true && !string.IsNullOrEmpty(outlineName))
                    return outlineName;
                if (_itemMap?.TryGetValue(entityId, out var itemName) == true && !string.IsNullOrEmpty(itemName))
                    return itemName;
                if (_secretMap?.TryGetValue(entityId, out var secretName) == true && !string.IsNullOrEmpty(secretName))
                    return secretName;
                if (_pledgeMap?.TryGetValue(entityId, out var pledgeName) == true && !string.IsNullOrEmpty(pledgeName))
                    return pledgeName;
                if (_deadlineMap?.TryGetValue(entityId, out var deadlineName) == true && !string.IsNullOrEmpty(deadlineName))
                    return deadlineName;
            }

            return "未知实体";
        }

        public static string ResolveCharacter(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return entityId;
            EnsureLoaded();
            lock (_lock)
            {
                if (_characterMap?.TryGetValue(entityId, out var name) == true && !string.IsNullOrEmpty(name))
                    return name;
            }
            return "未知角色";
        }

        public static string ResolveForeshadowing(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return entityId;
            EnsureLoaded();
            lock (_lock)
            {
                if (_foreshadowingMap?.TryGetValue(entityId, out var name) == true && !string.IsNullOrEmpty(name))
                    return name;
            }
            return "未知伏笔";
        }

        public static string ResolveConflict(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return entityId;
            EnsureLoaded();
            lock (_lock)
            {
                if (_conflictMap?.TryGetValue(entityId, out var name) == true && !string.IsNullOrEmpty(name))
                    return name;
            }
            return "未知冲突";
        }

        public static void Invalidate()
        {
            lock (_lock)
            {
                _lastLoadTime = DateTime.MinValue;
            }
        }

        public static void PreloadInBackground()
        {
            _ = System.Threading.Tasks.Task.Run(async () => await EnsureLoadedAsync().ConfigureAwait(false));
        }

        private static void EnsureLoaded()
        {
            if (DateTime.Now - _lastLoadTime < CacheExpiry &&
                _characterMap != null && _foreshadowingMap != null && _conflictMap != null)
                return;

            if (_characterMap != null && _foreshadowingMap != null && _conflictMap != null)
            {
                PreloadInBackground();
                return;
            }

            try
            {
                System.Threading.Tasks.Task.Run(async () => await EnsureLoadedAsync().ConfigureAwait(false))
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EntityNameResolver] EnsureLoaded 同步等待失败，回退异步预加载: {ex.Message}");
                PreloadInBackground();
            }
        }

        #region 异步方法（后台预加载使用，不阻塞线程池线程）

        private static async System.Threading.Tasks.Task EnsureLoadedAsync()
        {
            if (DateTime.Now - _lastLoadTime < CacheExpiry &&
                _characterMap != null && _foreshadowingMap != null && _conflictMap != null)
                return;

            await _coldLoadSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (DateTime.Now - _lastLoadTime < CacheExpiry &&
                    _characterMap != null && _foreshadowingMap != null && _conflictMap != null)
                { _coldLoadSemaphore.Release(); return; }
            }
            catch { _coldLoadSemaphore.Release(); return; }

            var charMap = new Dictionary<string, string>();
            var locationMap = new Dictionary<string, string>();
            var factionMap = new Dictionary<string, string>();
            var plotRuleMap = new Dictionary<string, string>();
            var foreshadowingMap = new Dictionary<string, string>();
            var conflictMap = new Dictionary<string, string>();
            var worldRuleMap = new Dictionary<string, string>();
            var volumeDesignMap = new Dictionary<string, string>();
            var chapterPlanMap = new Dictionary<string, string>();
            var blueprintMap = new Dictionary<string, string>();
            var outlineMap = new Dictionary<string, string>();
            var itemMap = new Dictionary<string, string>();
            var secretMap = new Dictionary<string, string>();
            var pledgeMap = new Dictionary<string, string>();
            var deadlineMap = new Dictionary<string, string>();

            var loadedAt = DateTime.MinValue;
            try
            {
                await LoadFromElementsAsync(charMap, locationMap, factionMap, plotRuleMap).ConfigureAwait(false);
                await LoadFromGlobalSettingsAsync(worldRuleMap).ConfigureAwait(false);
                await LoadFromGenerateElementsAsync(volumeDesignMap, chapterPlanMap, blueprintMap).ConfigureAwait(false);
                await LoadFromGuidesAsync(foreshadowingMap, conflictMap, outlineMap).ConfigureAwait(false);
                await LoadVolumeScopedGuideMapAsync(itemMap, "item_state_guide", "Items").ConfigureAwait(false);
                await LoadVolumeScopedGuideMapAsync(secretMap, "secret_reveal_guide", "Secrets").ConfigureAwait(false);
                await LoadVolumeScopedGuideMapAsync(pledgeMap, "pledge_constraint_guide", "Pledges").ConfigureAwait(false);
                await LoadVolumeScopedGuideMapAsync(deadlineMap, "deadline_constraint_guide", "Deadlines").ConfigureAwait(false);
                loadedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EntityNameResolver] 加载映射失败: {ex.Message}");
            }

            lock (_lock)
            {
                _characterMap = charMap;
                _locationMap = locationMap;
                _factionMap = factionMap;
                _plotRuleMap = plotRuleMap;
                _foreshadowingMap = foreshadowingMap;
                _conflictMap = conflictMap;
                _worldRuleMap = worldRuleMap;
                _volumeDesignMap = volumeDesignMap;
                _chapterPlanMap = chapterPlanMap;
                _blueprintMap = blueprintMap;
                _outlineMap = outlineMap;
                _itemMap = itemMap;
                _secretMap = secretMap;
                _pledgeMap = pledgeMap;
                _deadlineMap = deadlineMap;
                if (loadedAt != DateTime.MinValue) _lastLoadTime = loadedAt;
            }
            _coldLoadSemaphore.Release();
        }

        private static async System.Threading.Tasks.Task LoadFromElementsAsync(
            Dictionary<string, string> charMap,
            Dictionary<string, string> locationMap,
            Dictionary<string, string> factionMap,
            Dictionary<string, string> plotRuleMap)
        {
            var elementsPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "Design", "elements.json");
            if (!File.Exists(elementsPath)) return;

            var json = await File.ReadAllTextAsync(elementsPath).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data)) return;

            if (data.TryGetProperty("characterrules", out var charModule) &&
                charModule.TryGetProperty("character_rules", out var characters))
            {
                foreach (var item in characters.EnumerateArray())
                {
                    var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        charMap[id] = name;
                }
            }

            if (data.TryGetProperty("locationrules", out var locModule) &&
                locModule.TryGetProperty("location_rules", out var locations))
            {
                foreach (var item in locations.EnumerateArray())
                {
                    var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        locationMap[id] = name;
                }
            }

            if (data.TryGetProperty("factionrules", out var facModule) &&
                facModule.TryGetProperty("faction_rules", out var factions))
            {
                foreach (var item in factions.EnumerateArray())
                {
                    var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        factionMap[id] = name;
                }
            }

            if (data.TryGetProperty("plotrules", out var plotModule) &&
                plotModule.TryGetProperty("plot_rules", out var plotRules))
            {
                foreach (var item in plotRules.EnumerateArray())
                {
                    var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        plotRuleMap[id] = name;
                }
            }
        }

        private static async System.Threading.Tasks.Task LoadFromGuidesAsync(
            Dictionary<string, string> foreshadowingMap,
            Dictionary<string, string> conflictMap,
            Dictionary<string, string> outlineMap)
        {
            var guidesPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
            if (!Directory.Exists(guidesPath)) return;

            var foreshadowingPath = Path.Combine(guidesPath, "foreshadowing_status_guide.json");
            if (File.Exists(foreshadowingPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(foreshadowingPath).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Foreshadowings", out var foreshadowings))
                    {
                        foreach (var prop in foreshadowings.EnumerateObject())
                        {
                            var id = prop.Name;
                            var name = prop.Value.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                foreshadowingMap[id] = name;
                        }
                    }
                }
                catch { }
            }

            var conflictFiles = Directory.Exists(guidesPath)
                ? Directory.GetFiles(guidesPath, "conflict_progress_guide_vol*.json")
                : Array.Empty<string>();
            foreach (var conflictPath in conflictFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(conflictPath).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Conflicts", out var conflicts))
                    {
                        foreach (var prop in conflicts.EnumerateObject())
                        {
                            var id = prop.Name;
                            var name = prop.Value.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                conflictMap[id] = name;
                        }
                    }
                }
                catch { }
            }

            var outlinePath = Path.Combine(guidesPath, "outline_guide.json");
            if (File.Exists(outlinePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(outlinePath).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Volumes", out var volumes))
                    {
                        foreach (var prop in volumes.EnumerateObject())
                        {
                            var id = prop.Name;
                            var name = prop.Value.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                outlineMap[id] = name;
                        }
                    }
                }
                catch { }
            }
        }

        private static async System.Threading.Tasks.Task LoadVolumeScopedGuideMapAsync(
            Dictionary<string, string> targetMap,
            string baseName,
            string entitiesPropertyName)
        {
            var guidesPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
            if (!Directory.Exists(guidesPath)) return;

            var files = Directory.GetFiles(guidesPath, $"{baseName}_vol*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty(entitiesPropertyName, out var entities)
                        && entities.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in entities.EnumerateObject())
                        {
                            var id = prop.Name;
                            var name = prop.Value.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                targetMap[id] = name;
                        }
                    }
                }
                catch { }
            }
        }

        private static async System.Threading.Tasks.Task LoadFromGlobalSettingsAsync(Dictionary<string, string> worldRuleMap)
        {
            var settingsPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "Design", "globalsettings.json");
            if (!File.Exists(settingsPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(settingsPath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return;

                if (data.TryGetProperty("worldrules", out var worldModule) &&
                    worldModule.TryGetProperty("world_rules", out var worldRules))
                {
                    foreach (var item in worldRules.EnumerateArray())
                    {
                        var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                        var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            worldRuleMap[id] = name;
                    }
                }
            }
            catch { }
        }

        private static async System.Threading.Tasks.Task LoadFromGenerateElementsAsync(
            Dictionary<string, string> volumeDesignMap,
            Dictionary<string, string> chapterPlanMap,
            Dictionary<string, string> blueprintMap)
        {
            var elementsPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "Generate", "elements.json");
            if (!File.Exists(elementsPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(elementsPath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return;

                if (data.TryGetProperty("volumedesign", out var volModule) &&
                    volModule.TryGetProperty("volume_design_data", out var volDesigns))
                {
                    foreach (var item in volDesigns.EnumerateArray())
                    {
                        var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                        var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            volumeDesignMap[id] = name;
                    }
                }

                if (data.TryGetProperty("chapter", out var chapterModule) &&
                    chapterModule.TryGetProperty("chapter_data", out var chapters))
                {
                    foreach (var item in chapters.EnumerateArray())
                    {
                        var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                        var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            chapterPlanMap[id] = name;
                    }
                }

                if (data.TryGetProperty("blueprint", out var bpModule) &&
                    bpModule.TryGetProperty("blueprint_data", out var blueprints))
                {
                    foreach (var item in blueprints.EnumerateArray())
                    {
                        var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                        var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            blueprintMap[id] = name;
                    }
                }
            }
            catch { }
        }

        #endregion
    }
}

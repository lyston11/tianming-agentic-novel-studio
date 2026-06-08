using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideContextService
    {
        #region Cache

        public async Task InitializeCacheAsync()
        {
            if (_cacheInitialized) return;

            await _cacheInitLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cacheInitialized) return;

                var epoch = Volatile.Read(ref _cacheEpoch);

                var worldRulesTask = LoadPackagedAsync<Models.Design.Worldview.WorldRulesData>("Design/globalsettings.json", "worldrules");
                var charactersTask = LoadPackagedAsync<Models.Design.Characters.CharacterRulesData>("Design/elements.json", "characterrules");
                var factionsTask = LoadPackagedAsync<Models.Design.Factions.FactionRulesData>("Design/elements.json", "factionrules");
                var locationsTask = LoadPackagedAsync<Models.Design.Location.LocationRulesData>("Design/elements.json", "locationrules");
                var plotRulesTask = LoadPackagedAsync<Models.Design.Plot.PlotRulesData>("Design/elements.json", "plotrules");
                var volumesTask = LoadPackagedAsync<Models.Generate.StrategicOutline.OutlineData>("Generate/globalsettings.json", "outline");
                var chapterPlansTask = LoadPackagedAsync<ChapterData>("Generate/elements.json", "chapter");
                var blueprintsTask = LoadPackagedAsync<BlueprintData>("Generate/elements.json", "blueprint");
                var volumeDesignsTask = LoadPackagedAsync<VolumeDesignData>("Generate/elements.json", "volumedesign");
                var templatesTask = LoadTemplatesAsync();

                await Task.WhenAll(
                    worldRulesTask, charactersTask, factionsTask, locationsTask, plotRulesTask,
                    volumesTask, chapterPlansTask, blueprintsTask, volumeDesignsTask, templatesTask).ConfigureAwait(false);

                if (!IsCacheEpochCurrent(epoch))
                    return;

                foreach (var w in await worldRulesTask.ConfigureAwait(false))
                {
                    if (!IsCacheEpochCurrent(epoch)) return;
                    _worldRulesCache[w.Id] = w;
                }
                foreach (var c in await charactersTask.ConfigureAwait(false))
                {
                    if (!IsCacheEpochCurrent(epoch)) return;
                    _characterCache[c.Id] = c;
                }
                foreach (var f in await factionsTask.ConfigureAwait(false))
                {
                    if (!IsCacheEpochCurrent(epoch)) return;
                    _factionCache[f.Id] = f;
                }
                foreach (var l in await locationsTask.ConfigureAwait(false))
                {
                    if (!IsCacheEpochCurrent(epoch)) return;
                    _locationCache[l.Id] = l;
                }
                foreach (var p in await plotRulesTask.ConfigureAwait(false))
                {
                    if (!IsCacheEpochCurrent(epoch)) return;
                    _plotRulesCache[p.Id] = p;
                }
                foreach (var v in await volumesTask.ConfigureAwait(false))
                {
                    if (!IsCacheEpochCurrent(epoch)) return;
                    _volumeCache[v.Id] = v;
                }

                foreach (var plan in await chapterPlansTask.ConfigureAwait(false))
                {
                    if (!IsCacheEpochCurrent(epoch)) return;
                    if (!string.IsNullOrWhiteSpace(plan.Id))
                        _chapterPlanCache[plan.Id] = plan;
                }

                foreach (var blueprint in await blueprintsTask.ConfigureAwait(false))
                {
                    if (!IsCacheEpochCurrent(epoch)) return;
                    if (!string.IsNullOrWhiteSpace(blueprint.Id))
                        _blueprintCache[blueprint.Id] = blueprint;
                }

                foreach (var volumeDesign in await volumeDesignsTask.ConfigureAwait(false))
                {
                    if (!IsCacheEpochCurrent(epoch)) return;
                    if (!string.IsNullOrWhiteSpace(volumeDesign.Id))
                        _volumeDesignCache[volumeDesign.Id] = volumeDesign;
                }

                foreach (var template in await templatesTask.ConfigureAwait(false))
                {
                    if (!IsCacheEpochCurrent(epoch)) return;
                    _templateCache[template.Id] = template;
                }

                if (!IsCacheEpochCurrent(epoch))
                    return;

                _cacheInitialized = true;
                TM.App.Log("[GuideContextService] 缓存初始化完成（并行加载）");
            }
            finally
            {
                _cacheInitLock.Release();
            }
        }

        private async Task<List<CreativeMaterialData>> LoadTemplatesAsync()
        {
            try
            {
                var templatePath = StoragePathHelper.GetFilePath(
                    "Modules",
                    "Design/Templates/CreativeMaterials",
                    "creative_materials.json");
                if (File.Exists(templatePath))
                {
                    var json = await File.ReadAllTextAsync(templatePath).ConfigureAwait(false);
                    var templates = JsonSerializer.Deserialize<List<CreativeMaterialData>>(json, JsonOptions) ?? new List<CreativeMaterialData>();
                    return templates
                        .Where(template => template != null
                            && template.IsEnabled
                            && !string.IsNullOrWhiteSpace(template.Id))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载创作模板失败: {ex.Message}");
            }
            return new List<CreativeMaterialData>();
        }

        public void ClearCache()
        {
            Interlocked.Increment(ref _cacheEpoch);
            _characterCache.Clear();
            _worldRulesCache.Clear();
            _templateCache.Clear();
            _factionCache.Clear();
            _locationCache.Clear();
            _plotRulesCache.Clear();
            _volumeCache.Clear();
            _chapterPlanCache.Clear();
            _blueprintCache.Clear();
            _volumeDesignCache.Clear();
            lock (_contentGuideCacheLock)
            {
                _contentGuideCache = null;
            }
            _expansionConfig = null;
            _summaryStore.InvalidateCache();
            _milestoneStore.InvalidateCache();
            ServiceLocator.Get<VolumeFactArchiveStore>().InvalidateCache();
            ServiceLocator.Get<KeywordChapterIndexService>().InvalidateCache();
            ServiceLocator.Get<PlotPointsIndexService>().InvalidateCache();
            _cacheInitialized = false;
            lock (_chapterIdsCacheLock) { _chapterIdsCachedForPath = string.Empty; }
            TM.App.Log("[GuideContextService] 缓存已清除");
        }

        private static async System.Threading.Tasks.Task<string[]> GetCachedChapterIdsAsync(string chaptersPath)
        {
            lock (_chapterIdsCacheLock)
            {
                var now = DateTime.UtcNow;
                if (_chapterIdsCachedForPath == chaptersPath
                    && (now - _chapterIdsCachedAt).TotalSeconds < 30
                    && _cachedChapterIds.Length > 0)
                    return _cachedChapterIds;
            }

            var freshIds = await System.Threading.Tasks.Task.Run(() =>
                System.IO.Directory.Exists(chaptersPath)
                    ? System.IO.Directory.GetFiles(chaptersPath, "*.md", System.IO.SearchOption.TopDirectoryOnly)
                          .Select(f => System.IO.Path.GetFileNameWithoutExtension(f)).ToArray()
                    : Array.Empty<string>()).ConfigureAwait(false);

            lock (_chapterIdsCacheLock)
            {
                _cachedChapterIds = freshIds;
                _chapterIdsCachedForPath = chaptersPath;
                _chapterIdsCachedAt = DateTime.UtcNow;
                return _cachedChapterIds;
            }
        }

        #endregion
    }
}

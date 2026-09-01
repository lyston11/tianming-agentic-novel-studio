using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
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

                var worldRulesTask = LoadPackagedAsync<Models.Design.Worldview.WorldRulesData>(GuideRuntimeDataKeys.WorldRules);
                var charactersTask = LoadPackagedAsync<Models.Design.Characters.CharacterRulesData>(GuideRuntimeDataKeys.Characters);
                var factionsTask = LoadPackagedAsync<Models.Design.Factions.FactionRulesData>(GuideRuntimeDataKeys.Factions);
                var locationsTask = LoadPackagedAsync<Models.Design.Location.LocationRulesData>(GuideRuntimeDataKeys.Locations);
                var plotRulesTask = LoadPackagedAsync<Models.Design.Plot.PlotRulesData>(GuideRuntimeDataKeys.PlotRules);
                var volumesTask = LoadPackagedAsync<Models.Generate.StrategicOutline.OutlineData>(GuideRuntimeDataKeys.Outlines);
                var chapterPlansTask = LoadPackagedAsync<ChapterData>(GuideRuntimeDataKeys.ChapterPlans);
                var blueprintsTask = LoadPackagedAsync<BlueprintData>(GuideRuntimeDataKeys.Blueprints);
                var volumeDesignsTask = LoadPackagedAsync<VolumeDesignData>(GuideRuntimeDataKeys.VolumeDesigns);
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
            await Task.CompletedTask.ConfigureAwait(false);
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
            ServiceLocator.TryGet<IVolumeFactArchiveService>()?.InvalidateCache();
            ServiceLocator.TryGet<IPlotPointRecallService>()?.InvalidateCache();
            _cacheInitialized = false;
            TM.App.Log("[GuideContextService] 缓存已清除");
        }

        #endregion
    }
}

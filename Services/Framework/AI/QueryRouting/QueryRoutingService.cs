using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Modules.Generate.GlobalSettings.Outline.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.Elements.Blueprint.Services;

namespace TM.Services.Framework.AI.QueryRouting
{
    public class QueryRoutingService
    {
        private readonly IGuideContextService _guideService;
        private readonly DataIndexService _dataIndex;
        private readonly QueryRouter _router;
        private readonly IChangeDetectionService _changeDetection;
        private volatile bool _cacheInitialized;
        private int _cacheEpoch;
        private readonly SemaphoreSlim _cacheInitLock = new(1, 1);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public QueryRoutingService(
            IGuideContextService guideService,
            DataIndexService dataIndex,
            QueryRouter router,
            IChangeDetectionService changeDetection)
        {
            _guideService = guideService;
            _dataIndex = dataIndex;
            _router = router;
            _changeDetection = changeDetection;

            GuideContextService.CacheInvalidated += (_, _) => ClearCache();
        }

        public void ClearCache()
        {
            _guideService.ClearCache();
            _dataIndex.Clear();
            _cacheInitialized = false;
            Interlocked.Increment(ref _cacheEpoch);
        }

        private async Task EnsureCacheAsync()
        {
            if (_cacheInitialized)
                return;

            await _cacheInitLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cacheInitialized)
                    return;

                var epoch = Volatile.Read(ref _cacheEpoch);

                await _guideService.InitializeCacheAsync().ConfigureAwait(false);
                if (epoch != Volatile.Read(ref _cacheEpoch))
                    return;

                await _dataIndex.InitializeAsync().ConfigureAwait(false);
                if (epoch != Volatile.Read(ref _cacheEpoch))
                    return;

                var allNames = _dataIndex.ListIdsByCategory(EntityCategory.Character)
                    .Select(id => _dataIndex.FindById(id)?.Name ?? "")
                    .Concat(_dataIndex.ListIdsByCategory(EntityCategory.Location)
                        .Select(id => _dataIndex.FindById(id)?.Name ?? ""))
                    .Concat(_dataIndex.ListIdsByCategory(EntityCategory.Faction)
                        .Select(id => _dataIndex.FindById(id)?.Name ?? ""))
                    .Concat(_dataIndex.ListIdsByCategory(EntityCategory.PlotRule)
                        .Select(id => _dataIndex.FindById(id)?.Name ?? ""))
                    .Concat(_dataIndex.ListIdsByCategory(EntityCategory.WorldRule)
                        .Select(id => _dataIndex.FindById(id)?.Name ?? ""))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                if (epoch != Volatile.Read(ref _cacheEpoch))
                    return;

                _router.UpdateNameIndex(allNames);
                _cacheInitialized = true;
                TM.App.Log($"[QueryRoutingService] 索引初始化完成: {allNames.Count} 个实体名称");
            }
            finally
            {
                _cacheInitLock.Release();
            }
        }

        public async Task<string> GetCharacterByIdAsync(string characterId)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var results = await _guideService.ExtractCharactersAsync(new List<string> { characterId }).ConfigureAwait(false);
            if (results.Count == 0)
                return $"[未找到] 角色ID: {characterId}";
            return JsonSerializer.Serialize(results[0], JsonOptions);
        }

        public async Task<string> GetCharactersByIdsAsync(string characterIds)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var ids = characterIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim()).ToList();
            var results = await _guideService.ExtractCharactersAsync(ids).ConfigureAwait(false);
            if (results.Count == 0)
                return "[未找到] 未匹配到任何角色";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        private string ResolveDisplay(string id)
        {
            var entry = _dataIndex.FindById(id);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                return id;
            return $"{entry.Name}({entry.Id})";
        }

        public async Task<string> GetLocationByIdAsync(string locationId)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var results = await _guideService.ExtractLocationsAsync(new List<string> { locationId }).ConfigureAwait(false);
            if (results.Count == 0)
                return $"[未找到] 地点ID: {locationId}";
            return JsonSerializer.Serialize(results[0], JsonOptions);
        }

        public async Task<string> GetFactionByIdAsync(string factionId)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var results = await _guideService.ExtractFactionsAsync(new List<string> { factionId }).ConfigureAwait(false);
            if (results.Count == 0)
                return $"[未找到] 势力ID: {factionId}";
            return JsonSerializer.Serialize(results[0], JsonOptions);
        }

        public async Task<string> GetPlotRuleByIdAsync(string plotRuleId)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var results = await _guideService.ExtractPlotRulesAsync(new List<string> { plotRuleId }).ConfigureAwait(false);
            if (results.Count == 0)
                return $"[未找到] 剧情规则ID: {plotRuleId}";
            return JsonSerializer.Serialize(results[0], JsonOptions);
        }

        public async Task<string> GetWorldRuleByIdAsync(string worldRuleId)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var results = await _guideService.ExtractWorldRulesAsync(new List<string> { worldRuleId }).ConfigureAwait(false);
            if (results.Count == 0)
                return $"[未找到] 世界观规则ID: {worldRuleId}";
            return JsonSerializer.Serialize(results[0], JsonOptions);
        }

        public async Task<string> GetExpandedChapterContextAsync(string chapterId)
        {
            var ctx = await _guideService.BuildContentContextAsync(chapterId, default).ConfigureAwait(false);
            if (ctx == null)
                return $"[未找到] 章节上下文: {chapterId}，请确认已执行打包";
            return JsonSerializer.Serialize(ctx, JsonOptions);
        }

        public async Task<string> GetChapterContextAsync(string chapterId)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var guide = await _guideService.GetContentGuideAsync().ConfigureAwait(false);
            if (guide?.Chapters == null || !guide.Chapters.TryGetValue(chapterId, out var entry))
                return $"[未找到] 章节: {chapterId}";
            return JsonSerializer.Serialize(new { entry.ChapterId, entry.Title, entry.Summary, entry.ContextIds }, JsonOptions);
        }

        public async Task<string> GetLocationsByIdsAsync(string locationIds)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var ids = locationIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = await _guideService.ExtractLocationsAsync(ids).ConfigureAwait(false);
            if (results.Count == 0)
                return "[未找到] 未匹配到任何地点";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        public async Task<string> GetFactionsByIdsAsync(string factionIds)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var ids = factionIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = await _guideService.ExtractFactionsAsync(ids).ConfigureAwait(false);
            if (results.Count == 0)
                return "[未找到] 未匹配到任何势力";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        public async Task<string> GetPlotRulesByIdsAsync(string plotRuleIds)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var ids = plotRuleIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = await _guideService.ExtractPlotRulesAsync(ids).ConfigureAwait(false);
            if (results.Count == 0)
                return "[未找到] 未匹配到任何剧情规则";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        public async Task<string> GetWorldRulesByIdsAsync(string worldRuleIds)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var ids = worldRuleIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = await _guideService.ExtractWorldRulesAsync(ids).ConfigureAwait(false);
            if (results.Count == 0)
                return "[未找到] 未匹配到任何世界观规则";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        public async Task<string> ListAvailableIdsAsync(string category)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var ids = category.ToLowerInvariant() switch
            {
                "characters" => _dataIndex.ListIdsByCategory(EntityCategory.Character),
                "locations" => _dataIndex.ListIdsByCategory(EntityCategory.Location),
                "factions" => _dataIndex.ListIdsByCategory(EntityCategory.Faction),
                "plotrules" => _dataIndex.ListIdsByCategory(EntityCategory.PlotRule),
                "worldrules" => _dataIndex.ListIdsByCategory(EntityCategory.WorldRule),
                _ => null
            };

            if (ids == null)
            {
                var directList = category.ToLowerInvariant() switch
                {
                    "templates" => ServiceLocator.TryGet<TM.Modules.Design.Templates.CreativeMaterials.Services.CreativeMaterialsService>()
                        ?.GetAllMaterials().Select(d => new { d.Id, d.Name, Display = $"{d.Name}({d.Id})" }).Cast<object>().ToList(),
                    "outline" => ServiceLocator.TryGet<OutlineService>()?.GetAllOutlines()
                        .Select(d => new { d.Id, d.Name, Display = $"{d.Name}({d.Id})" }).Cast<object>().ToList(),
                    "volumedesign" => ServiceLocator.TryGet<VolumeDesignService>()?.GetAllVolumeDesigns()
                        .Select(d => new { d.Id, d.Name, Display = $"{d.Name}({d.Id})" }).Cast<object>().ToList(),
                    "chapter" => ServiceLocator.TryGet<ChapterService>()?.GetAllChapters()
                        .Select(d => new { d.Id, d.Name, Display = $"{d.Name}({d.Id})" }).Cast<object>().ToList(),
                    "blueprint" => ServiceLocator.TryGet<BlueprintService>()?.GetAllBlueprints()
                        .Select(d => new { d.Id, d.Name, Display = $"{d.Name}({d.Id})" }).Cast<object>().ToList(),
                    _ => null
                };
                if (directList == null || directList.Count == 0)
                    return $"[未找到] 类别 {category} 无可用数据";
                return JsonSerializer.Serialize(directList, JsonOptions);
            }

            var list = ids
                .Select(id => _dataIndex.FindById(id))
                .Where(entry => entry != null)
                .Select(entry => new { entry!.Id, entry.Name, Display = $"{entry.Name}({entry.Id})" })
                .ToList();
            if (list.Count == 0)
                return $"[未找到] 类别 {category} 无可用数据";
            return JsonSerializer.Serialize(list, JsonOptions);
        }

        public async Task<string> ValidateDataConsistencyAsync()
        {
            await _changeDetection.RefreshAllAsync().ConfigureAwait(false);

            var changedModules = _changeDetection.GetChangedModules();
            if (changedModules.Count > 0)
            {
                return $"[警告] 以下模块有未打包变更：{string.Join(", ", changedModules.Select(m => m.Replace("Generate", "生成").Replace("Design", "设计").Replace("GlobalSettings", "全局设置").Replace("Elements", "元素")))}";
            }
            return "[正常] 打包数据与原始数据一致";
        }

        public async Task<string> SearchCharactersAsync(string query, int topK = 5)
        {
            await EnsureCacheAsync().ConfigureAwait(false);

            var indexResults = _dataIndex.SearchByCategory(EntityCategory.Character, query, topK);
            if (indexResults.Count > 0)
            {
                var matched = indexResults
                    .Select(e => new { e.Id, e.Name, Display = $"{e.Name}({e.Id})", Brief = "" })
                    .ToList();
                return JsonSerializer.Serialize(matched, JsonOptions);
            }

            var allCharacters = await _guideService.ExtractCharactersAsync(new List<string>()).ConfigureAwait(false);
            var fallbackMatched = allCharacters
                .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           c.Identity?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                           c.FlawBelief?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(topK)
                .Select(c => new { c.Id, c.Name, Display = $"{c.Name}({c.Id})", Brief = c.Identity ?? "" })
                .ToList();

            if (fallbackMatched.Count == 0)
                return $"[未找到] 无匹配角色: {query}";
            return JsonSerializer.Serialize(fallbackMatched, JsonOptions);
        }

        public async Task<string> SearchLocationsAsync(string query, int topK = 5)
        {
            await EnsureCacheAsync().ConfigureAwait(false);

            var indexResults = _dataIndex.SearchByCategory(EntityCategory.Location, query, topK);
            if (indexResults.Count > 0)
            {
                var matched = indexResults.Select(e => new { e.Id, e.Name, Display = $"{e.Name}({e.Id})", Brief = "" }).ToList();
                return JsonSerializer.Serialize(matched, JsonOptions);
            }

            var allLocations = await _guideService.ExtractLocationsAsync(new List<string>()).ConfigureAwait(false);
            var fallbackMatched = allLocations
                .Where(l => l.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           l.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(topK)
                .Select(l => new { l.Id, l.Name, Display = $"{l.Name}({l.Id})", Brief = l.Description ?? "" })
                .ToList();

            if (fallbackMatched.Count == 0)
                return $"[未找到] 无匹配地点: {query}";
            return JsonSerializer.Serialize(fallbackMatched, JsonOptions);
        }

        public async Task<string> SearchFactionsAsync(string query, int topK = 5)
        {
            await EnsureCacheAsync().ConfigureAwait(false);

            var indexResults = _dataIndex.SearchByCategory(EntityCategory.Faction, query, topK);
            if (indexResults.Count > 0)
            {
                var matched = indexResults.Select(e => new { e.Id, e.Name, Display = $"{e.Name}({e.Id})", Brief = "" }).ToList();
                return JsonSerializer.Serialize(matched, JsonOptions);
            }

            var allFactions = await _guideService.ExtractFactionsAsync(new List<string>()).ConfigureAwait(false);
            var fallbackMatched = allFactions
                .Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           f.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(topK)
                .Select(f => new { f.Id, f.Name, Display = $"{f.Name}({f.Id})", Brief = f.Description ?? "" })
                .ToList();

            if (fallbackMatched.Count == 0)
                return $"[未找到] 无匹配势力: {query}";
            return JsonSerializer.Serialize(fallbackMatched, JsonOptions);
        }

        public async Task<string> SearchWorldRulesAsync(string query, int topK = 5)
        {
            await EnsureCacheAsync().ConfigureAwait(false);

            var indexResults = _dataIndex.SearchByCategory(EntityCategory.WorldRule, query, topK);
            if (indexResults.Count > 0)
            {
                var matched = indexResults.Select(e => new { e.Id, e.Name, Display = $"{e.Name}({e.Id})", Brief = "" }).ToList();
                return JsonSerializer.Serialize(matched, JsonOptions);
            }

            var allWorldRules = await _guideService.ExtractWorldRulesAsync(new List<string>()).ConfigureAwait(false);
            var fallbackMatched = allWorldRules
                .Where(w => w.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           w.OneLineSummary?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                           w.PowerSystem?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(topK)
                .Select(w => new { w.Id, w.Name, Display = $"{w.Name}({w.Id})", Brief = w.OneLineSummary ?? "" })
                .ToList();

            if (fallbackMatched.Count == 0)
                return $"[未找到] 无匹配世界观规则: {query}";
            return JsonSerializer.Serialize(fallbackMatched, JsonOptions);
        }

        public async Task<string> SearchPlotRulesAsync(string query, int topK = 5)
        {
            await EnsureCacheAsync().ConfigureAwait(false);

            var indexResults = _dataIndex.SearchByCategory(EntityCategory.PlotRule, query, topK);
            if (indexResults.Count > 0)
            {
                var matched = indexResults.Select(e => new { e.Id, e.Name, Display = $"{e.Name}({e.Id})", Brief = "" }).ToList();
                return JsonSerializer.Serialize(matched, JsonOptions);
            }

            var allPlotRules = await _guideService.ExtractPlotRulesAsync(new List<string>()).ConfigureAwait(false);
            var fallbackMatched = allPlotRules
                .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           p.Goal?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                           p.Conflict?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(topK)
                .Select(p => new { p.Id, p.Name, Display = $"{p.Name}({p.Id})", Brief = p.OneLineSummary ?? p.Goal ?? "" })
                .ToList();

            if (fallbackMatched.Count == 0)
                return $"[未找到] 无匹配剧情规则: {query}";
            return JsonSerializer.Serialize(fallbackMatched, JsonOptions);
        }

        public async Task<string> SearchContentAsync(string query, int topK = 5)
        {
            var search = ServiceLocator.Get<ContentChunkSearchService>();
            var hits = await search.SearchAsync(query, topK).ConfigureAwait(false);
            if (hits.Count == 0)
                return $"[未找到] 无匹配正文内容: {query}";

            var results = hits.Select(h => new
            {
                ChapterId = h.ChapterId,
                Position = h.Position,
                Content = h.Content,
                Score = h.Score
            });
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        public async Task<string> FindRelatedChaptersAsync(string description)
        {
            var keywordIndex = ServiceLocator.Get<KeywordChapterIndexService>();
            var keywords = (description ?? string.Empty)
                .Split(new[] { ' ', '\n', '、', '，', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToList();
            if (keywords.Count == 0)
                return $"[未找到] 无相关章节: {description}";

            var chapterIds = await keywordIndex.SearchAsync(keywords, 10).ConfigureAwait(false);
            if (chapterIds.Count == 0)
                return $"[未找到] 无相关章节: {description}";
            return JsonSerializer.Serialize(chapterIds, JsonOptions);
        }

        public Task<string> GetCreativeMaterialsByIdsAsync(string materialIds)
        {
            var service = ServiceLocator.TryGet<TM.Modules.Design.Templates.CreativeMaterials.Services.CreativeMaterialsService>();
            if (service == null) return Task.FromResult("[错误] CreativeMaterialsService 未注册");
            var ids = materialIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = service.GetAllMaterials().Where(d => ids.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (results.Count == 0) return Task.FromResult("[未找到] 未匹配到任何创作素材");
            return Task.FromResult(JsonSerializer.Serialize(results, JsonOptions));
        }

        public Task<string> GetOutlinesByIdsAsync(string outlineIds)
        {
            var service = ServiceLocator.TryGet<OutlineService>();
            if (service == null) return Task.FromResult("[错误] OutlineService 未注册");
            var ids = outlineIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = service.GetAllOutlines().Where(d => ids.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (results.Count == 0) return Task.FromResult("[未找到] 未匹配到任何大纲");
            return Task.FromResult(JsonSerializer.Serialize(results, JsonOptions));
        }

        public Task<string> GetVolumeDesignsByIdsAsync(string volumeIds)
        {
            var service = ServiceLocator.TryGet<VolumeDesignService>();
            if (service == null) return Task.FromResult("[错误] VolumeDesignService 未注册");
            var ids = volumeIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = service.GetAllVolumeDesigns().Where(d => ids.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (results.Count == 0) return Task.FromResult("[未找到] 未匹配到任何分卷");
            return Task.FromResult(JsonSerializer.Serialize(results, JsonOptions));
        }

        public Task<string> GetChapterPlansByIdsAsync(string chapterIds)
        {
            var service = ServiceLocator.TryGet<ChapterService>();
            if (service == null) return Task.FromResult("[错误] ChapterService 未注册");
            var ids = chapterIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = service.GetAllChapters().Where(d => ids.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (results.Count == 0) return Task.FromResult("[未找到] 未匹配到任何章节规划");
            return Task.FromResult(JsonSerializer.Serialize(results, JsonOptions));
        }

        public Task<string> GetBlueprintsByIdsAsync(string blueprintIds)
        {
            var service = ServiceLocator.TryGet<BlueprintService>();
            if (service == null) return Task.FromResult("[错误] BlueprintService 未注册");
            var ids = blueprintIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = service.GetAllBlueprints().Where(d => ids.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (results.Count == 0) return Task.FromResult("[未找到] 未匹配到任何蓝图");
            return Task.FromResult(JsonSerializer.Serialize(results, JsonOptions));
        }

        public Task<string> GetOutlineByIdAsync(string outlineId)
        {
            var service = ServiceLocator.TryGet<OutlineService>();
            if (service == null) return Task.FromResult("[错误] OutlineService 未注册");
            var item = service.GetAllOutlines().FirstOrDefault(d => string.Equals(d.Id, outlineId, StringComparison.OrdinalIgnoreCase));
            if (item == null) return Task.FromResult($"[未找到] 大纲ID: {outlineId}");
            return Task.FromResult(JsonSerializer.Serialize(item, JsonOptions));
        }

        public Task<string> SearchOutlinesAsync(string query, int topK = 5)
        {
            var service = ServiceLocator.TryGet<OutlineService>();
            if (service == null) return Task.FromResult("[错误] OutlineService 未注册");
            var all = service.GetAllOutlines().Where(d => d.IsEnabled).ToList();
            if (!string.IsNullOrWhiteSpace(query))
            {
                all = all.Where(d =>
                    d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.OneLineOutline.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.Theme.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.CoreConflict.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (all.Count == 0) return Task.FromResult($"[未找到] 无匹配大纲: {query}");
            var result = all.Take(topK).Select(d => new { d.Id, d.Name, Display = $"{d.Name}({d.Id})", Brief = d.OneLineOutline }).ToList();
            return Task.FromResult(JsonSerializer.Serialize(result, JsonOptions));
        }

        public Task<string> GetVolumeDesignByIdAsync(string volumeId)
        {
            var service = ServiceLocator.TryGet<VolumeDesignService>();
            if (service == null) return Task.FromResult("[错误] VolumeDesignService 未注册");
            var item = service.GetAllVolumeDesigns().FirstOrDefault(d => string.Equals(d.Id, volumeId, StringComparison.OrdinalIgnoreCase));
            if (item == null) return Task.FromResult($"[未找到] 分卷ID: {volumeId}");
            return Task.FromResult(JsonSerializer.Serialize(item, JsonOptions));
        }

        public Task<string> SearchVolumeDesignsAsync(string query, int topK = 5)
        {
            var service = ServiceLocator.TryGet<VolumeDesignService>();
            if (service == null) return Task.FromResult("[错误] VolumeDesignService 未注册");
            var all = service.GetAllVolumeDesigns().Where(d => d.IsEnabled).ToList();
            if (!string.IsNullOrWhiteSpace(query))
            {
                all = all.Where(d =>
                    d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.VolumeTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.VolumeTheme.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.MainConflict.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (all.Count == 0) return Task.FromResult($"[未找到] 无匹配分卷: {query}");
            var result = all.Take(topK).Select(d => new { d.Id, d.Name, Display = $"{d.Name}({d.Id})", Brief = d.VolumeTitle }).ToList();
            return Task.FromResult(JsonSerializer.Serialize(result, JsonOptions));
        }

        public Task<string> GetChapterPlanByIdAsync(string chapterId)
        {
            var service = ServiceLocator.TryGet<ChapterService>();
            if (service == null) return Task.FromResult("[错误] ChapterService 未注册");
            var item = service.GetAllChapters().FirstOrDefault(d => string.Equals(d.Id, chapterId, StringComparison.OrdinalIgnoreCase));
            if (item == null) return Task.FromResult($"[未找到] 章节规划ID: {chapterId}");
            return Task.FromResult(JsonSerializer.Serialize(item, JsonOptions));
        }

        public Task<string> SearchChapterPlansAsync(string query, int topK = 10)
        {
            var service = ServiceLocator.TryGet<ChapterService>();
            if (service == null) return Task.FromResult("[错误] ChapterService 未注册");
            var all = service.GetAllChapters().Where(d => d.IsEnabled).ToList();
            if (!string.IsNullOrWhiteSpace(query))
            {
                all = all.Where(d =>
                    d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.ChapterTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.ChapterTheme.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.Volume.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (all.Count == 0) return Task.FromResult($"[未找到] 无匹配章节规划: {query}");
            var result = all.Take(topK).Select(d => new { d.Id, d.Name, Display = $"{d.Name}({d.Id})", Brief = d.ChapterTitle }).ToList();
            return Task.FromResult(JsonSerializer.Serialize(result, JsonOptions));
        }

        public Task<string> GetBlueprintByIdAsync(string blueprintId)
        {
            var service = ServiceLocator.TryGet<BlueprintService>();
            if (service == null) return Task.FromResult("[错误] BlueprintService 未注册");
            var item = service.GetAllBlueprints().FirstOrDefault(d => string.Equals(d.Id, blueprintId, StringComparison.OrdinalIgnoreCase));
            if (item == null) return Task.FromResult($"[未找到] 蓝图ID: {blueprintId}");
            return Task.FromResult(JsonSerializer.Serialize(item, JsonOptions));
        }

        public Task<string> SearchBlueprintsAsync(string query, int topK = 10)
        {
            var service = ServiceLocator.TryGet<BlueprintService>();
            if (service == null) return Task.FromResult("[错误] BlueprintService 未注册");
            var all = service.GetAllBlueprints().Where(d => d.IsEnabled).ToList();
            if (!string.IsNullOrWhiteSpace(query))
            {
                all = all.Where(d =>
                    d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.OneLineStructure.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.ChapterId.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (all.Count == 0) return Task.FromResult($"[未找到] 无匹配蓝图: {query}");
            var result = all.Take(topK).Select(d => new { d.Id, d.Name, Display = $"{d.Name}({d.Id})", Brief = d.OneLineStructure }).ToList();
            return Task.FromResult(JsonSerializer.Serialize(result, JsonOptions));
        }

        public async Task<string> SmartSearchAsync(string query)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var routeResult = _router.RouteWithDetails(query);

            return routeResult.Route switch
            {
                QueryRoute.Precise => await PreciseSearchAsync(routeResult).ConfigureAwait(false),
                QueryRoute.Semantic => await SemanticSearchAsync(query).ConfigureAwait(false),
                _ => await HybridSearchAsync(query, routeResult).ConfigureAwait(false)
            };
        }

        private async Task<string> PreciseSearchAsync(QueryRouteResult routeResult)
        {
            await EnsureCacheAsync().ConfigureAwait(false);
            var results = new List<object>();

            foreach (var chapterId in routeResult.ChapterIds)
            {
                var ctx = await GetExpandedChapterContextAsync(chapterId).ConfigureAwait(false);
                if (!ctx.StartsWith("[未找到]"))
                    results.Add(new { Type = "chapter", Id = chapterId, Display = chapterId, Data = ctx });
            }

            foreach (var entityId in routeResult.EntityIds)
            {
                var display = ResolveDisplay(entityId);
                var character = await GetCharacterByIdAsync(entityId).ConfigureAwait(false);
                if (!character.StartsWith("[未找到]"))
                {
                    results.Add(new { Type = "character", Id = entityId, Display = display, Data = character });
                    continue;
                }

                var location = await GetLocationByIdAsync(entityId).ConfigureAwait(false);
                if (!location.StartsWith("[未找到]"))
                {
                    results.Add(new { Type = "location", Id = entityId, Display = display, Data = location });
                    continue;
                }

                var plotRule = await GetPlotRuleByIdAsync(entityId).ConfigureAwait(false);
                if (!plotRule.StartsWith("[未找到]"))
                    results.Add(new { Type = "plotRule", Id = entityId, Display = display, Data = plotRule });
            }

            foreach (var name in routeResult.MatchedNames)
            {
                var characters = await SearchCharactersAsync(name, 1).ConfigureAwait(false);
                if (!characters.StartsWith("[未找到]"))
                    results.Add(new { Type = "characterSearch", Query = name, Display = name, Data = characters });
            }

            if (results.Count == 0)
                return $"[未找到] 无匹配结果: {routeResult.Query}";

            return JsonSerializer.Serialize(results, JsonOptions);
        }

        private async Task<string> SemanticSearchAsync(string query)
        {
            return await SearchContentAsync(query, 5).ConfigureAwait(false);
        }

        private async Task<string> HybridSearchAsync(string query, QueryRouteResult routeResult)
        {
            var preciseResults = await PreciseSearchAsync(routeResult).ConfigureAwait(false);
            var semanticResults = await SemanticSearchAsync(query).ConfigureAwait(false);

            return JsonSerializer.Serialize(new
            {
                Precise = preciseResults,
                Semantic = semanticResults
            }, JsonOptions);
        }
    }
}

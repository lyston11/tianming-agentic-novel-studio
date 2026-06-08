using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Services.Modules.ProjectData.Models.Index;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class FocusContextService : IFocusContextService
    {
        private readonly IndexService _indexService;
        private readonly RelationStrengthService _relationStrengthService;
        private readonly IGuideContextService _guideContextService;
        private readonly GlobalSummaryService _globalSummaryService;

        private readonly object _cacheLock = new();
        private readonly Dictionary<string, (DateTime Time, DesignFocusContext Context)> _designContextCache = new();
        private readonly Dictionary<string, (DateTime Time, GenerateFocusContext Context)> _generateContextCache = new();
        private readonly TimeSpan _contextCacheExpiry = TimeSpan.FromSeconds(30);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public FocusContextService(
            IndexService indexService,
            RelationStrengthService relationStrengthService,
            IGuideContextService guideContextService,
            GlobalSummaryService globalSummaryService)
        {
            _indexService = indexService;
            _relationStrengthService = relationStrengthService;
            _guideContextService = guideContextService;
            _globalSummaryService = globalSummaryService;

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FocusContextService] 订阅项目切换事件失败: {ex.Message}");
            }

            GuideContextService.CacheInvalidated += (_, _) => InvalidateCache();
        }

        public async Task<DesignFocusContext> GetDesignContextAsync(string focusId, string targetLayer)
        {
            var cacheKey = BuildCacheKey("Design", focusId, targetLayer);
            lock (_cacheLock)
            {
                if (_designContextCache.TryGetValue(cacheKey, out var cached)
                    && DateTime.UtcNow - cached.Time < _contextCacheExpiry)
                {
                    return cached.Context;
                }
            }

            var globalSummaryTask = GetGlobalSummaryAsync();
            var trackingTask = GetTrackingStatusAsync();
            var upstreamTask = _indexService.BuildUpstreamIndexAsync(targetLayer);
            var focusTask = BuildFocusContextAsync(focusId, targetLayer);
            await Task.WhenAll(globalSummaryTask, trackingTask, upstreamTask, focusTask).ConfigureAwait(false);

            var upstream = await upstreamTask.ConfigureAwait(false);
            var focus = await focusTask.ConfigureAwait(false);
            focus.UpstreamIndex = upstream;

            var context = new DesignFocusContext
            {
                GlobalSummary = await globalSummaryTask.ConfigureAwait(false),
                TrackingStatus = await trackingTask.ConfigureAwait(false),
                UpstreamIndex = upstream,
                Focus = focus
            };

            if (InfoLogDedup.ShouldLog($"FocusContextService:Built:{targetLayer}"))
                TM.App.Log($"[FocusContextService] 设计上下文已构建: targetLayer={targetLayer}, 上下文长度≈{EstimateContextLength(context)}字符");

            lock (_cacheLock)
            {
                _designContextCache[cacheKey] = (DateTime.UtcNow, context);
            }

            return context;
        }

        public async Task<GenerateFocusContext> GetGenerateContextAsync(string focusId, string targetLayer)
        {
            var cacheKey = BuildCacheKey("Generate", focusId, targetLayer);
            lock (_cacheLock)
            {
                if (_generateContextCache.TryGetValue(cacheKey, out var cached)
                    && DateTime.UtcNow - cached.Time < _contextCacheExpiry)
                {
                    return cached.Context;
                }
            }

            var globalSummaryTask = GetGlobalSummaryAsync();
            var trackingTask = GetTrackingStatusAsync();
            var upstreamTask = _indexService.BuildUpstreamIndexAsync(targetLayer);
            var focusTask = BuildFocusContextAsync(focusId, targetLayer);
            var taskContextTask = LoadTaskContextAsync(focusId, targetLayer);
            await Task.WhenAll(globalSummaryTask, trackingTask, upstreamTask, focusTask, taskContextTask).ConfigureAwait(false);

            var upstream = await upstreamTask.ConfigureAwait(false);
            var focus = await focusTask.ConfigureAwait(false);
            focus.UpstreamIndex = upstream;

            var context = new GenerateFocusContext
            {
                GlobalSummary = await globalSummaryTask.ConfigureAwait(false),
                TrackingStatus = await trackingTask.ConfigureAwait(false),
                UpstreamIndex = upstream,
                Focus = focus,
                TaskContext = await taskContextTask.ConfigureAwait(false)
            };

            TM.App.Log($"[FocusContextService] 创作上下文已构建: targetLayer={targetLayer}, 上下文长度≈{EstimateContextLength(context)}字符");

            lock (_cacheLock)
            {
                _generateContextCache[cacheKey] = (DateTime.UtcNow, context);
            }

            return context;
        }

        public async Task<GlobalSummary> GetGlobalSummaryAsync()
        {
            return await _globalSummaryService.GetGlobalSummaryAsync().ConfigureAwait(false);
        }

        public async Task<TrackingStatus> GetTrackingStatusAsync()
        {
            return await BuildTrackingStatusRealtimeAsync().ConfigureAwait(false);
        }

        private async Task<FocusContext> BuildFocusContextAsync(string focusId, string targetLayer)
        {
            var focus = new FocusContext
            {
                FocusId = focusId,
                FocusType = targetLayer,
                Layer = targetLayer,
                DirectRelations = new List<IndexItem>(),
                IndirectRelations = new List<IndexItem>()
            };

            if (!string.IsNullOrEmpty(focusId))
            {
                var focusItemTask = _indexService.GetIndexItemAsync(focusId, targetLayer);
                var relatedTask = _guideContextService.GetRelatedEntitiesAsync(focusId, targetLayer);
                await Task.WhenAll(focusItemTask, relatedTask).ConfigureAwait(false);

                var focusItem = await focusItemTask.ConfigureAwait(false);
                if (focusItem != null)
                {
                    focus.FocusEntity = focusItem;
                }

                var (direct, indirect) = await relatedTask.ConfigureAwait(false);
                focus.DirectRelations = direct;
                focus.IndirectRelations = indirect;

                if (focus.DirectRelations.Count == 0 && focus.IndirectRelations.Count == 0)
                {
                    await LoadRelationsViaStrengthServiceAsync(focus, focusId).ConfigureAwait(false);
                }
            }

            return focus;
        }

        private static string BuildCacheKey(string kind, string focusId, string targetLayer)
            => $"{kind}|{targetLayer}|{focusId}";

        private async Task LoadRelationsViaStrengthServiceAsync(FocusContext focus, string focusId)
        {
            var allCharacterIds = await GetAllCharacterIdsAsync().ConfigureAwait(false);
            var candidates = allCharacterIds.Where(id => id != focusId).ToList();

            var strengthTasks = candidates.Select(charId =>
                _relationStrengthService.GetStrengthAsync(focusId, charId).ContinueWith(t =>
                    (charId, strength: t.Result), TaskContinuationOptions.ExecuteSynchronously));
            var results = await Task.WhenAll(strengthTasks).ConfigureAwait(false);

            foreach (var (charId, strength) in results)
            {
                if (strength == RelationStrength.Strong && focus.DirectRelations.Count < 5)
                {
                    var item = await _indexService.GetIndexItemAsync(charId, "Characters").ConfigureAwait(false);
                    if (item != null)
                    {
                        item.RelationStrength = "Strong";
                        focus.DirectRelations.Add(item);
                    }
                }
                else if (strength == RelationStrength.Medium && focus.IndirectRelations.Count < 10)
                {
                    var item = await _indexService.GetIndexItemAsync(charId, "Characters").ConfigureAwait(false);
                    if (item != null)
                    {
                        item.RelationStrength = "Medium";
                        focus.IndirectRelations.Add(item);
                    }
                }
            }
        }

        private async Task<List<string>> GetAllCharacterIdsAsync()
        {
            try
            {
                var allChars = await _guideContextService.GetAllCharactersAsync().ConfigureAwait(false);
                return allChars
                    .Select(c => c.Id)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FocusContextService] 加载角色ID列表失败: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task<TrackingStatus> BuildTrackingStatusRealtimeAsync()
        {
            var status = new TrackingStatus();

            try
            {
                var guideManager = ServiceLocator.Get<GuideManager>();

                var characterGuideTask = guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.CharacterStateGuide>(
                    "character_state_guide.json");
                var conflictGuideTask = guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.ConflictProgressGuide>(
                    "conflict_progress_guide.json");
                var foreshadowingGuideTask = guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.ForeshadowingStatusGuide>(
                    "foreshadowing_status_guide.json");
                var plotPointService = ServiceLocator.Get<PlotPointsIndexService>();
                var plotVolTasks = plotPointService.GetExistingVolumeNumbers()
                    .Select(vol => plotPointService.GetVolumeEntriesAsync(vol)).ToArray();

                await Task.WhenAll(
                    characterGuideTask, conflictGuideTask, foreshadowingGuideTask,
                    Task.WhenAll(plotVolTasks)).ConfigureAwait(false);

                var characterGuide = characterGuideTask.Result;
                var conflictGuide = conflictGuideTask.Result;
                var foreshadowingGuide = foreshadowingGuideTask.Result;
                var recentPlotEntries = new System.Collections.Generic.List<TM.Services.Modules.ProjectData.Models.Tracking.PlotPointEntry>();
                foreach (var volTask in plotVolTasks)
                    recentPlotEntries.AddRange(volTask.Result);
                var plotPoints = new Models.Guides.PlotPointsIndex { PlotPoints = recentPlotEntries };

                status.CharacterStates = characterGuide.Characters
                    .Select(kvp =>
                    {
                        var last = kvp.Value.StateHistory.LastOrDefault();
                        return new CharacterState
                        {
                            CharacterId = kvp.Key,
                            CharacterName = kvp.Value.Name,
                            CurrentStatus = last == null
                                ? string.Empty
                                : string.Join("/", new[] { last.Phase, last.Level, last.MentalState }.Where(s => !string.IsNullOrWhiteSpace(s))),
                            LastAppearanceChapter = last?.Chapter ?? string.Empty,
                            CurrentGoal = string.Empty
                        };
                    })
                    .Where(s => !string.IsNullOrWhiteSpace(s.CharacterId))
                    .OrderByDescending(s => s.LastAppearanceChapter, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                    .Take(30)
                    .ToList();

                static int MapProgressPercent(string? status)
                {
                    return (status ?? string.Empty).ToLowerInvariant() switch
                    {
                        "resolved" => 100,
                        "climax" => 80,
                        "active" => 50,
                        _ => 0
                    };
                }

                status.ConflictProgress = conflictGuide.Conflicts
                    .Select(kvp => new ConflictProgress
                    {
                        ConflictId = kvp.Key,
                        ConflictName = kvp.Value.Name,
                        ProgressPercent = MapProgressPercent(kvp.Value.Status),
                        CurrentPhase = kvp.Value.Status ?? string.Empty,
                        NextExpectedEvent = string.Empty
                    })
                    .Where(c => !string.IsNullOrWhiteSpace(c.ConflictId))
                    .Take(30)
                    .ToList();

                status.ForeshadowingStats = new ForeshadowingStats
                {
                    Total = foreshadowingGuide.Summary.Total,
                    Planted = foreshadowingGuide.Summary.Setup,
                    Resolved = foreshadowingGuide.Summary.Payoff,
                    PendingResolution = foreshadowingGuide.PendingList
                        .Select(p => p.Id)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct()
                        .Take(50)
                        .ToList()
                };

                status.PlotPoints = plotPoints;

                if (InfoLogDedup.ShouldLog("FocusContextService:TrackingStatus"))
                    TM.App.Log("[FocusContextService] TrackingStatus实时构建完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FocusContextService] 构建TrackingStatus失败: {ex.Message}");
            }

            return status;
        }

        private async Task<object?> LoadTaskContextAsync(string focusId, string targetLayer)
        {
            return targetLayer switch
            {
                "Blueprint" => await _guideContextService.BuildBlueprintContextAsync(focusId).ConfigureAwait(false),
                "Content" => await _guideContextService.BuildContentContextAsync(focusId, default).ConfigureAwait(false),
                _ => null
            };
        }

        public void InvalidateCache()
        {
            _globalSummaryService.InvalidateCache();
            _relationStrengthService.InvalidateCache();

            lock (_cacheLock)
            {
                _designContextCache.Clear();
                _generateContextCache.Clear();
            }
        }

        #region 按层级入口方法

        public Task<DesignFocusContext> GetSmartParsingContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "SmartParsing");

        public Task<DesignFocusContext> GetTemplatesContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "Templates");

        public Task<DesignFocusContext> GetWorldviewContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "Worldview");

        public Task<DesignFocusContext> GetCharactersContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "Characters");

        public Task<DesignFocusContext> GetFactionsContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "Factions");

        public Task<DesignFocusContext> GetPlotContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "Plot");

        public Task<GenerateFocusContext> GetOutlineContextAsync(string focusId)
            => GetGenerateContextAsync(focusId, "Outline");

        public Task<GenerateFocusContext> GetPlanningContextAsync(string focusId)
            => GetGenerateContextAsync(focusId, "Planning");

        public Task<GenerateFocusContext> GetBlueprintContextAsync(string focusId)
            => GetGenerateContextAsync(focusId, "Blueprint");

        public Task<GenerateFocusContext> GetContentContextAsync(string focusId)
            => GetGenerateContextAsync(focusId, "Content");

        #endregion

        private int EstimateContextLength(DesignFocusContext context)
        {
            var length = 0;
            if (context.GlobalSummary != null)
                length += (context.GlobalSummary.ToString()?.Length ?? 0);
            if (context.UpstreamIndex != null)
                length += EstimateUpstreamIndexLength(context.UpstreamIndex);
            return length;
        }

        private int EstimateContextLength(GenerateFocusContext context)
        {
            var length = 0;
            if (context.GlobalSummary != null)
                length += (context.GlobalSummary.ToString()?.Length ?? 0);
            if (context.UpstreamIndex != null)
                length += EstimateUpstreamIndexLength(context.UpstreamIndex);
            return length;
        }

        private int EstimateUpstreamIndexLength(Models.Index.UpstreamIndex index)
        {
            var length = 0;
            length += index.SmartParsing?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Templates?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Worldview?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Characters?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Factions?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Plot?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Outline?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Planning?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Blueprint?.Sum(i => i.BriefSummary.Length) ?? 0;
            return length;
        }
    }
}

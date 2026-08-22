using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class FactSnapshotExtractor
    {
        #region 公开方法

        public async Task<FactSnapshot> ExtractSnapshotAsync(
            string chapterId,
            List<string> characterIds,
            List<string> locationIds,
            List<string> conflictIds,
            List<string> foreshadowingSetupIds,
            List<string> foreshadowingPayoffIds,
            List<string> worldRuleIds,
            List<string>? factionIds = null)
        {
            var cfg = LayeredContextConfig.TakeSnapshot();
            var snapshot = new FactSnapshot();
            var prevChapterId = GetPreviousChapterId(chapterId);
            var characterIdSet = characterIds != null && characterIds.Count > 0
                ? new HashSet<string>(characterIds, StringComparer.Ordinal)
                : null;

            var otherEntityIds = new HashSet<string>();
            otherEntityIds.UnionWith(conflictIds);
            otherEntityIds.UnionWith(foreshadowingSetupIds);
            otherEntityIds.UnionWith(foreshadowingPayoffIds);

            var t1 = ExtractCharacterStatesAsync(characterIds, prevChapterId, cfg);
            var t2 = ExtractConflictProgressAsync(conflictIds);
            var t3 = ExtractForeshadowingStatusAsync(foreshadowingSetupIds, foreshadowingPayoffIds);
            var t4 = ExtractPlotPointsAsync(chapterId, characterIds, otherEntityIds.ToList());
            var t5 = ExtractCharacterDescriptionsAsync(characterIds);
            var t6 = ExtractLocationDescriptionsAsync(locationIds);
            var t7 = ExtractWorldRuleConstraintsAsync(worldRuleIds);
            var t8 = ExtractLocationStatesAsync(locationIds, prevChapterId: prevChapterId, cfg: cfg);
            var t9 = ExtractFactionStatesAsync(applyLimit: true, priorityIds: factionIds, prevChapterId: prevChapterId, cfg: cfg);
            var t10 = ExtractTimelineAsync(cfg: cfg);
            var t11 = ExtractCharacterLocationsAsync(prevChapterId, forceIncludeCharacterIds: characterIds, cfg: cfg);
            var t12 = ExtractItemStatesAsync(cfg, characterIds: characterIds);
            var t13 = ServiceLocator.Get<SecretRevealService>().ExtractSnapshotAsync(chapterId);
            var t14 = ServiceLocator.Get<PledgeConstraintService>().ExtractSnapshotAsync(chapterId);
            var t15 = ServiceLocator.Get<DeadlineConstraintService>().ExtractSnapshotAsync(chapterId);

            await Task.WhenAll(t1, t2, t3, t4, t5, t6, t7, t8, t9, t10, t11, t12, t13, t14, t15).ConfigureAwait(false);

            snapshot.CharacterStates = t1.Result.Count > cfg.SnapshotMaxCharacterInject
                ? t1.Result.Take(cfg.SnapshotMaxCharacterInject).ToList()
                : t1.Result;
            snapshot.ConflictProgress = t2.Result.Count > cfg.SnapshotMaxConflictInject
                ? t2.Result.Take(cfg.SnapshotMaxConflictInject).ToList()
                : t2.Result;
            snapshot.ForeshadowingStatus = t3.Result.Count > cfg.SnapshotMaxForeshadowInject
                ? t3.Result.Take(cfg.SnapshotMaxForeshadowInject).ToList()
                : t3.Result;
            snapshot.PlotPoints = t4.Result;
            snapshot.CharacterDescriptions = t5.Result;
            snapshot.LocationDescriptions = t6.Result;
            snapshot.WorldRuleConstraints = t7.Result;
            snapshot.LocationStates = t8.Result.Count > cfg.SnapshotMaxLocationInject
                ? t8.Result.Take(cfg.SnapshotMaxLocationInject).ToList()
                : t8.Result;
            snapshot.FactionStates = t9.Result;
            snapshot.Timeline = t10.Result;
            snapshot.CharacterLocations = t11.Result;
            snapshot.ItemStates = t12.Result;
            snapshot.SecretStates = ApplyRelevanceLimit(
                t13.Result, cfg.SnapshotMaxSecretInject, characterIdSet,
                s => s.KnowerIds ?? Enumerable.Empty<string>(),
                s => s.ChapterId);
            snapshot.PledgeStates = ApplyRelevanceLimit(
                t14.Result, cfg.SnapshotMaxPledgeInject, characterIdSet,
                p => SplitCsv(p.PartyIds),
                p => p.ChapterId,
                p => p.IsOverdue);
            snapshot.DeadlineStates = ApplyRelevanceLimit(
                t15.Result, cfg.SnapshotMaxDeadlineInject, characterIdSet,
                d => SplitCsv(d.PartyIds),
                d => d.ChapterId,
                d => d.IsOverdue);

            if (snapshot.CharacterStates != null && snapshot.CharacterDescriptions != null)
            {
                var activeInjectedIds = snapshot.CharacterStates
                    .Select(s => s.Id)
                    .Where(id => (characterIdSet == null || !characterIdSet.Contains(id)) && !snapshot.CharacterDescriptions.ContainsKey(id))
                    .ToList();

                if (activeInjectedIds.Count > 0)
                {
                    var extraDescs = await ExtractCharacterDescriptionsAsync(activeInjectedIds).ConfigureAwait(false);
                    foreach (var (id, desc) in extraDescs)
                        snapshot.CharacterDescriptions[id] = desc;

                    TM.App.Log($"[FactSnapshotExtractor] 补充注入活跃角色描述: {activeInjectedIds.Count}条");
                }
            }

            return snapshot;
        }

        public async Task<Dictionary<string, CharacterCoreDescription>> ExtractCharacterDescriptionsAsync(List<string>? characterIds)
        {
            var result = new Dictionary<string, CharacterCoreDescription>();
            if (characterIds == null || characterIds.Count == 0)
                return result;

            try
            {
                var guideService = GuideContextService;
                var characters = await guideService.ExtractCharactersAsync(characterIds).ConfigureAwait(false);

                foreach (var c in characters)
                {
                    var appearance = c.Appearance ?? string.Empty;
                    result[c.Id] = new CharacterCoreDescription
                    {
                        Id = c.Id,
                        Name = c.Name,
                        HairColor = ExtractHairColor(appearance),
                        EyeColor = string.Empty,
                        Appearance = appearance,
                        PersonalityTags = ParseTags(c.FlawBelief + "," + c.Identity + "," + c.Want)
                    };
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取角色描述失败: {ex.Message}");
            }

            return result;
        }

        public async Task<Dictionary<string, LocationCoreDescription>> ExtractLocationDescriptionsAsync(List<string> locationIds)
        {
            var result = new Dictionary<string, LocationCoreDescription>();
            if (locationIds == null || locationIds.Count == 0)
                return result;

            try
            {
                var guideService = GuideContextService;
                var locations = await guideService.ExtractLocationsAsync(locationIds).ConfigureAwait(false);

                foreach (var loc in locations)
                {
                    result[loc.Id] = new LocationCoreDescription
                    {
                        Id = loc.Id,
                        Name = loc.Name,
                        Description = loc.Description ?? string.Empty,
                        Features = ParseTags(loc.Description)
                    };
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取地点描述失败: {ex.Message}");
            }

            return result;
        }

        public async Task<List<WorldRuleConstraint>> ExtractWorldRuleConstraintsAsync(List<string> worldRuleIds)
        {
            var result = new List<WorldRuleConstraint>();

            if (worldRuleIds == null || worldRuleIds.Count == 0)
                return result;

            try
            {
                var guideService = GuideContextService;
                await guideService.InitializeCacheAsync().ConfigureAwait(false);
                var worldRules = await guideService.ExtractWorldRulesAsync(worldRuleIds).ConfigureAwait(false);

                foreach (var rule in worldRules)
                {
                    if (!string.IsNullOrEmpty(rule.HardRules))
                    {
                        result.Add(new WorldRuleConstraint
                        {
                            RuleId = rule.Id,
                            RuleName = rule.Name,
                            Constraint = rule.HardRules,
                            IsHardConstraint = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取世界观约束失败: {ex.Message}");
            }

            return result;
        }

        private static List<string> ParseTags(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();

            return text.Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0 && t.Length <= 20)
                .ToList();
        }

        private static string ExtractHairColor(string appearance)
        {
            if (string.IsNullOrWhiteSpace(appearance)) return string.Empty;
            foreach (var keyword in HairColorConstants.HairColorKeywords)
            {
                if (appearance.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return keyword;
            }
            return string.Empty;
        }

        private static IEnumerable<string> SplitCsv(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return Enumerable.Empty<string>();
            return csv.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0);
        }

        private static List<T> ApplyRelevanceLimit<T>(
            List<T> source,
            int maxInject,
            HashSet<string>? priorityIds,
            Func<T, IEnumerable<string>> partyAccessor,
            Func<T, string> chapterIdAccessor,
            Func<T, bool>? overdueAccessor = null)
        {
            if (source == null || source.Count == 0) return source ?? new List<T>();
            if (source.Count <= maxInject) return source;

            var comparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
            bool HasRelevantParty(T item)
            {
                if (priorityIds == null || priorityIds.Count == 0) return false;
                foreach (var p in partyAccessor(item))
                    if (!string.IsNullOrWhiteSpace(p) && priorityIds.Contains(p))
                        return true;
                return false;
            }
            return source
                .OrderByDescending(s => overdueAccessor != null && overdueAccessor(s) ? 1 : 0)
                .ThenByDescending(s => HasRelevantParty(s) ? 1 : 0)
                .ThenByDescending(s => chapterIdAccessor(s) ?? string.Empty, comparer)
                .Take(maxInject)
                .ToList();
        }

        #endregion
    }
}

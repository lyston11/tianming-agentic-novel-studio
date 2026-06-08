using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Factions;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideIndexBuilder
    {
        #region 4个追踪指导

        public async Task<CharacterStateGuide> BuildCharacterStateGuideAsync()
        {
            var guide = new CharacterStateGuide { Module = "CharacterStateGuide" };

            var profiles = await LoadAllAsync<CharacterRulesData>("Design/Elements/CharacterRules").ConfigureAwait(false);

            EnsureRequiredIds(profiles, p => p.Id, "角色规则", p => p.Name);
            EnsureRequiredCategoryIds(profiles, p => p.Category, p => p.CategoryId, "角色规则", p => p.Name);

            foreach (var profile in profiles)
            {
                guide.Characters[profile.Id] = new CharacterStateEntry
                {
                    Name = profile.Name,
                    BaseProfile = profile.Id,
                    StateHistory = new List<CharacterState>
                    {
                        new CharacterState
                        {
                            Chapter = "init",
                            Phase = "起",
                            Level = "初始",
                            Abilities = new List<string>(),
                            Relationships = new Dictionary<string, RelationshipState>(),
                            MentalState = string.IsNullOrEmpty(profile.FlawBelief) ? "普通" : profile.FlawBelief,
                            KeyEvent = "故事开始"
                        }
                    }
                };
            }

            TM.App.Log($"[GuideIndexBuilder] 角色状态追踪初始化完成，共{guide.Characters.Count}个角色");
            return guide;
        }

        public async Task<ConflictProgressGuide> BuildConflictProgressGuideAsync()
        {
            var guide = new ConflictProgressGuide { Module = "ConflictProgressGuide" };

            var plotRules = await LoadAllAsync<PlotRulesData>("Design/Elements/PlotRules").ConfigureAwait(false);

            EnsureRequiredIds(plotRules, p => p.Id, "剧情规则", p => p.Name);
            EnsureRequiredCategoryIds(plotRules, p => p.Category, p => p.CategoryId, "剧情规则", p => p.Name);

            foreach (var plotRule in plotRules.Where(p => !string.IsNullOrEmpty(p.Conflict)))
            {
                guide.Conflicts[plotRule.Id] = new ConflictProgressEntry
                {
                    Name = plotRule.Name,
                    Type = plotRule.EventType ?? "剧情事件",
                    Tier = "Tier-3",
                    Status = "pending",
                    ProgressPoints = new List<ConflictProgressPoint>(),
                    InvolvedChapters = new List<string>(),
                    InvolvedCharacters = (plotRule.MainCharacters ?? string.Empty)
                        .Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim()).ToList()
                };
            }

            TM.App.Log($"[GuideIndexBuilder] 冲突进度追踪初始化完成，共{guide.Conflicts.Count}个冲突");
            return guide;
        }

        public async Task<PlotPointsIndex> BuildPlotPointsIndexAsync()
        {
            var guide = new PlotPointsIndex { Module = "PlotPointsIndex" };

            guide.Keywords = new Dictionary<string, KeywordEntry>();
            guide.ChapterIndex = new Dictionary<string, List<string>>();

            TM.App.Log("[GuideIndexBuilder] 关键情节索引初始化完成");
            await Task.CompletedTask.ConfigureAwait(false);
            return guide;
        }

        public async Task<ForeshadowingStatusGuide> BuildForeshadowingStatusGuideAsync()
        {
            var guide = new ForeshadowingStatusGuide { Module = "ForeshadowingStatusGuide" };

            var plotRules = await LoadAllAsync<PlotRulesData>("Design/Elements/PlotRules").ConfigureAwait(false);

            EnsureRequiredIds(plotRules, p => p.Id, "剧情规则", p => p.Name);
            EnsureRequiredCategoryIds(plotRules, p => p.Category, p => p.CategoryId, "剧情规则", p => p.Name);

            int total = plotRules.Count;
            int setup = plotRules.Count(p => !string.IsNullOrEmpty(p.StoryPhase));
            int resolved = plotRules.Count(p => !string.IsNullOrEmpty(p.Result));

            foreach (var plotRule in plotRules)
            {
                var sp = plotRule.StoryPhase?.Trim() ?? string.Empty;
                var isChapterIdPhase = !string.IsNullOrEmpty(sp)
                    && ChapterParserHelper.ParseChapterId(sp).HasValue;
                var expectedSetupChapter = isChapterIdPhase ? sp : string.Empty;

                guide.Foreshadowings[plotRule.Id] = new ForeshadowingStatusEntry
                {
                    Name = plotRule.Name,
                    Tier = "Tier-3",
                    IsSetup = !string.IsNullOrEmpty(sp),
                    IsResolved = !string.IsNullOrEmpty(plotRule.Result),
                    IsOverdue = false,
                    ExpectedSetupChapter = expectedSetupChapter,
                    ExpectedPayoffChapter = string.Empty
                };
            }

            guide.Summary = new ForeshadowingSummary
            {
                Total = total,
                Setup = setup,
                Payoff = resolved,
                Pending = total - resolved,
                CompletionRate = total > 0 ? $"{(resolved * 100.0 / total):F1}%" : "0%"
            };

            TM.App.Log($"[GuideIndexBuilder] 伏笔完成度追踪初始化完成，共{total}个剧情规则");
            return guide;
        }

        public async Task<LocationStateGuide> BuildLocationStateGuideAsync()
        {
            var guide = new LocationStateGuide { Module = "LocationStateGuide" };

            var locations = await LoadAllAsync<LocationRulesData>("Design/Elements/LocationRules").ConfigureAwait(false);
            EnsureRequiredIds(locations, l => l.Id, "地点", l => l.Name);

            foreach (var loc in locations)
            {
                guide.Locations[loc.Id] = new LocationStateEntry
                {
                    Name = loc.Name,
                    CurrentStatus = "normal"
                };
            }

            TM.App.Log($"[GuideIndexBuilder] 地点状态追踪初始化完成，共{locations.Count}个地点");
            return guide;
        }

        public async Task<FactionStateGuide> BuildFactionStateGuideAsync()
        {
            var guide = new FactionStateGuide { Module = "FactionStateGuide" };

            var factions = await LoadAllAsync<FactionRulesData>("Design/Elements/FactionRules").ConfigureAwait(false);
            EnsureRequiredIds(factions, f => f.Id, "势力", f => f.Name);

            foreach (var faction in factions)
            {
                guide.Factions[faction.Id] = new FactionStateEntry
                {
                    Name = faction.Name,
                    CurrentStatus = "active"
                };
            }

            TM.App.Log($"[GuideIndexBuilder] 势力状态追踪初始化完成，共{factions.Count}个势力");
            return guide;
        }

        public async Task<TimelineGuide> BuildTimelineGuideAsync()
        {
            var guide = new TimelineGuide { Module = "TimelineGuide" };

            TM.App.Log("[GuideIndexBuilder] 时间线追踪初始化完成");
            await Task.CompletedTask.ConfigureAwait(false);
            return guide;
        }

        #endregion
    }
}

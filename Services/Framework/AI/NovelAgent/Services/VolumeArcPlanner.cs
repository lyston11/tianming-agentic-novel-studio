using System;
using System.Collections.Generic;
using System.Linq;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class VolumeArcPlanner
    {
        public VolumeArcPlan BuildPlan(
            VolumeArcPlanningRequest request,
            StoryCreativeConstitution? constitution,
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            var safeRequest = request ?? new VolumeArcPlanningRequest();
            var volumeId = string.IsNullOrWhiteSpace(safeRequest.VolumeId)
                ? "vol1"
                : safeRequest.VolumeId.Trim();
            var brief = BuildBriefText(safeRequest, constitution);
            var forbidden = ExtractForbiddenDirections(safeRequest);
            var isPowerProgression = ContainsAny(brief, "打怪", "升级", "刷怪", "突破", "怪潮", "碾压", "资源点", "境界");
            var title = string.IsNullOrWhiteSpace(safeRequest.VolumeTitle)
                ? $"{volumeId}：{BuildDefaultVolumeTitle(brief, isPowerProgression)}"
                : safeRequest.VolumeTitle.Trim();
            var expectedCount = Math.Clamp(safeRequest.ExpectedChapterCount, 6, 40);
            var genre = constitution?.Genre ?? string.Empty;
            var promise = FirstNonEmpty(
                safeRequest.UserGoal,
                constitution?.ReaderPromise,
                "建立本卷核心困境，并让主角以可追踪代价完成一次状态跃迁。");
            var conflict = FirstNonEmpty(
                constitution?.MainConflictEngine,
                "主角目标、世界规则和对手利益持续挤压。");

            var plan = new VolumeArcPlan
            {
                VolumeId = volumeId,
                Title = title,
                StartChapterId = string.IsNullOrWhiteSpace(safeRequest.StartChapterId)
                    ? $"{volumeId}_ch1"
                    : safeRequest.StartChapterId.Trim(),
                EndChapterId = string.IsNullOrWhiteSpace(safeRequest.EndChapterId)
                    ? $"{volumeId}_ch{expectedCount}"
                    : safeRequest.EndChapterId.Trim(),
                ExpectedChapterCount = expectedCount,
                Status = VolumeArcStatus.Proposed,
                VolumePromise = promise,
                EntryState = $"主角带着未解决的核心缺口进入本卷：{FirstNonEmpty(constitution?.ProtagonistEngine, "能力、认知或关系存在短板。")}",
                ExitState = isPowerProgression
                    ? "本卷结束时必须改变至少一个成长变量：境界、装备、资源地盘、敌人层级或队伍结构。"
                    : "本卷结束时必须改变至少一个长期变量：身份、阵营、世界规则认知或关系结构。",
                CoreQuestion = isPowerProgression
                    ? $"在“{promise}”压力下，主角如何通过战斗、资源和选择完成可见成长？"
                    : $"在“{promise}”压力下，主角愿意付出什么代价继续前进？",
                MainConflictUpgrade = $"将主线冲突从局部目标升级为卷级压力：{conflict}",
                MidpointReversal = BuildMidpointTurn(constitution, knowledge, isPowerProgression),
                Climax = BuildClimax(constitution, genre, isPowerProgression),
                AftermathHook = isPowerProgression
                    ? "卷末胜利必须打开更高层地图、资源需求或敌人层级，作为下一卷开口。"
                    : "卷末胜利必须留下新债务、新规则或新敌我边界，作为下一卷开口。",
                ChapterBeats = BuildBeats(expectedCount, promise, conflict, safeRequest.CandidateDirections, forbidden, isPowerProgression),
                ForeshadowingPlan = BuildForeshadowing(expectedCount, safeRequest.CandidateDirections, forbidden, isPowerProgression),
                CharacterArcPlan = BuildCharacterArcs(constitution, isPowerProgression),
                WorldbuildingIncrements = BuildWorldbuildingIncrements(constitution, knowledge, isPowerProgression),
                MustAvoid = BuildMustAvoid(safeRequest, constitution, knowledge)
            };

            return plan;
        }

        private static List<VolumeChapterBeat> BuildBeats(
            int expectedCount,
            string promise,
            string conflict,
            IReadOnlyCollection<string> candidateDirections,
            IReadOnlyCollection<string> forbidden,
            bool isPowerProgression)
        {
            var roles = BuildBeatRoles(candidateDirections, forbidden, isPowerProgression);

            var beats = new List<VolumeChapterBeat>();
            for (var i = 1; i <= expectedCount; i++)
            {
                var ratio = expectedCount == 1 ? 1 : (double)(i - 1) / (expectedCount - 1);
                var role = roles[Math.Min(roles.Count - 1, (int)Math.Round(ratio * (roles.Count - 1)))];
                beats.Add(new VolumeChapterBeat
                {
                    Index = i,
                    Role = role,
                    Goal = BuildBeatGoal(role, promise, isPowerProgression),
                    Turn = BuildBeatTurn(role, conflict, isPowerProgression),
                    Cost = BuildBeatCost(role, isPowerProgression)
                });
            }

            return beats;
        }

        private static List<string> BuildBeatRoles(
            IReadOnlyCollection<string> candidateDirections,
            IReadOnlyCollection<string> forbidden,
            bool isPowerProgression)
        {
            var roles = new List<string> { "开卷钩子" };
            roles.AddRange(candidateDirections
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(CleanDirection)
                .Where(x => !IsForbidden(x, forbidden)));

            if (isPowerProgression)
            {
                roles.AddRange(new[]
                {
                    "怪物压力升级",
                    "资源点争夺",
                    "境界突破门槛",
                    "强敌压迫",
                    "阶段战力兑现",
                    "更高地图开口"
                });
            }
            else
            {
                roles.AddRange(new[]
                {
                    "关键限制显形",
                    "多方压力挤压",
                    "中段信息改写",
                    "策略重组",
                    "卷末压迫升级",
                    "高潮选择",
                    "余波与新钩子"
                });
            }

            return roles
                .Where(x => !string.IsNullOrWhiteSpace(x) && !IsForbidden(x, forbidden))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .DefaultIfEmpty("目标推进")
                .ToList();
        }

        private static string BuildBeatGoal(string role, string promise, bool isPowerProgression)
        {
            if (isPowerProgression)
            {
                if (ContainsAny(role, "怪物", "战斗"))
                    return $"用一场改变局势的战斗兑现本卷承诺：{promise}";
                if (ContainsAny(role, "资源"))
                    return "让主角争夺具体资源，并让收益改变战力、装备或地图权限。";
                if (ContainsAny(role, "境界", "突破"))
                    return "设置明确突破门槛，并让主角用行动跨过一个成长台阶。";
                if (ContainsAny(role, "强敌"))
                    return "让更高层敌人压迫主角，证明旧战力不够用。";
                if (ContainsAny(role, "兑现"))
                    return "集中兑现前文积累的能力、资源和战斗选择。";
                if (ContainsAny(role, "地图"))
                    return "确认阶段胜利，并打开更高层区域、敌人或资源目标。";
            }

            return role switch
            {
                "开卷钩子" => $"用一个不可忽视的事件兑现本卷承诺：{promise}",
                "关键限制显形" => "展示本卷关键限制如何制造压力，而不是只做背景说明。",
                "中段信息改写" => "改写主角对目标、敌人或环境的一个关键判断。",
                "高潮选择" => "让主角用主动选择而非巧合解决卷级压力。",
                "余波与新钩子" => "确认本卷状态改变，并打开下一卷更大的问题。",
                _ => "推进一个可追踪的冲突变量，避免原地踏步。"
            };
        }

        private static string BuildBeatTurn(string role, string conflict, bool isPowerProgression)
        {
            if (isPowerProgression)
            {
                if (ContainsAny(role, "资源"))
                    return "敌人、怪物或女角色阵营加入争夺，让资源不再是顺手可拿。";
                if (ContainsAny(role, "境界", "突破"))
                    return "突破条件迫使主角改变战斗策略、资源分配或行动路线。";
                if (ContainsAny(role, "强敌"))
                    return $"把卷内冲突推向不可回避：{conflict}";
                if (ContainsAny(role, "地图"))
                    return "胜利打开更高层区域，同时暴露下一阶段威胁。";
            }

            return role switch
            {
                "多方压力挤压" => "外部冲突转化为立场、资源或行动路线冲突。",
                "策略重组" => "压力迫使主角调整策略、盟友或价值判断。",
                "卷末压迫升级" => $"把卷内冲突推向不可回避：{conflict}",
                _ => "让信息、关系、资源或环境认知发生变化。"
            };
        }

        private static string BuildBeatCost(string role, bool isPowerProgression)
        {
            if (isPowerProgression)
            {
                if (ContainsAny(role, "战斗", "怪物"))
                    return "消耗体力、装备耐久或暴露部分实力。";
                if (ContainsAny(role, "资源"))
                    return "获得收益，同时引来下一波抢夺者或资源债务。";
                if (ContainsAny(role, "境界", "突破"))
                    return "突破成功但留下短期不稳定、消耗或身份暴露。";
                if (ContainsAny(role, "强敌"))
                    return "以阶段胜利换来更强敌人的锁定。";
            }

            return role switch
            {
                "中段信息改写" => "旧计划作废，并暴露更深层威胁。",
                "高潮选择" => "以不可逆代价换取卷级状态变化。",
                _ => "至少产生一个后续章节可追踪的小后果。"
            };
        }

        private static List<VolumeForeshadowPlan> BuildForeshadowing(
            int expectedCount,
            IReadOnlyCollection<string> candidateDirections,
            IReadOnlyCollection<string> forbidden,
            bool isPowerProgression)
        {
            var plans = isPowerProgression
                ? new List<VolumeForeshadowPlan>
                {
                    new()
                    {
                        Name = "高阶怪物痕迹",
                        Setup = "前 1/4 卷投放一个超出当前战力层级的怪物痕迹。",
                        Payoff = "卷末证明该痕迹通向更高层地图或敌人。",
                        PayoffBeatIndex = Math.Max(3, expectedCount - 2)
                    },
                    new()
                    {
                        Name = "资源点归属",
                        Setup = "前半卷展示一个被多方盯上的资源点。",
                        Payoff = "中后段让资源点收益改变主角战力或队伍结构。",
                        PayoffBeatIndex = Math.Max(2, expectedCount / 2)
                    },
                    new()
                    {
                        Name = "突破门槛",
                        Setup = "提前说明突破需要的资源、战斗经验或身体承受条件。",
                        Payoff = "高潮前后兑现突破，并留下下一阶段的敌人注意。",
                        PayoffBeatIndex = Math.Max(4, expectedCount - 1)
                    }
                }
                : new List<VolumeForeshadowPlan>
                {
                    new()
                    {
                        Name = "关键限制异常",
                        Setup = "前 1/4 卷投放一个看似小的限制异常。",
                        Payoff = "中段证明异常不是漏洞，而是深层压力入口。",
                        PayoffBeatIndex = Math.Max(2, expectedCount / 2)
                    },
                    new()
                    {
                        Name = "人物立场裂痕",
                        Setup = "前半卷让一个盟友或对手的选择留下解释空间。",
                        Payoff = "卷末把该选择转化为立场变化或后果回收。",
                        PayoffBeatIndex = Math.Max(3, expectedCount - 2)
                    },
                    new()
                    {
                        Name = "后果账本",
                        Setup = "每次阶段推进后记录一个小后果。",
                        Payoff = "高潮前让多个小后果汇合成必须处理的主压力。",
                        PayoffBeatIndex = Math.Max(4, expectedCount - 1)
                    }
                };

            plans.AddRange(candidateDirections
                .Where(x => !string.IsNullOrWhiteSpace(x) && !IsForbidden(x, forbidden))
                .Take(2)
                .Select((direction, index) => new VolumeForeshadowPlan
                {
                    Name = CleanDirection(direction),
                    Setup = $"围绕“{CleanDirection(direction)}”提前放一个可追踪细节。",
                    Payoff = $"在本卷中后段兑现“{CleanDirection(direction)}”带来的行动变化。",
                    PayoffBeatIndex = Math.Max(2, Math.Min(expectedCount, expectedCount / 2 + index + 1))
                }));

            return plans
                .Where(plan => !IsForbidden($"{plan.Name} {plan.Setup} {plan.Payoff}", forbidden))
                .GroupBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(5)
                .ToList();
        }

        private static List<VolumeCharacterArc> BuildCharacterArcs(StoryCreativeConstitution? constitution, bool isPowerProgression)
        {
            if (isPowerProgression)
                return new List<VolumeCharacterArc>
                {
                    new()
                    {
                        CharacterName = "主角",
                        StartingBelief = FirstNonEmpty(constitution?.ProtagonistEngine, "只要稳住发育，就能靠系统和战斗推进。"),
                        Pressure = "怪物层级、资源竞争和身份暴露同时压迫。",
                        Choice = "在稳健发育和抢占关键资源之间做主动选择。",
                        ChangedState = "从被动求生转为主动规划战斗收益和成长路线。"
                    },
                    new()
                    {
                        CharacterName = "核心对手/强敌代表",
                        StartingBelief = "认为主角只是低阶幸存者或可掠夺资源。",
                        Pressure = "主角的成长速度破坏其原有判断。",
                        Choice = "升级追杀、抢夺资源或暴露更高层势力。",
                        ChangedState = "从局部威胁升级为下一阶段地图入口。"
                    }
                };

            return new List<VolumeCharacterArc>
            {
                new()
                {
                    CharacterName = "主角",
                    StartingBelief = FirstNonEmpty(constitution?.ProtagonistEngine, "只要目标正确，就能靠能力推进。"),
                    Pressure = "外部压力、对手利益和行动后果同时压迫。",
                    Choice = "接受不完美胜利，并主动承担后续后果。",
                    ChangedState = "从被动应对压力，转为主动利用局势并承认后果。"
                },
                new()
                {
                    CharacterName = "核心对手/镜像角色",
                    StartingBelief = "认为主角只能在既定局势内挣扎。",
                    Pressure = "主角的选择破坏其原有判断。",
                    Choice = "升级手段或暴露更高层立场。",
                    ChangedState = "从局部阻碍升级为卷级冲突代表。"
                }
            };
        }

        private static List<string> BuildWorldbuildingIncrements(
            StoryCreativeConstitution? constitution,
            CreativeKnowledgeRetrievalResult? knowledge,
            bool isPowerProgression)
        {
            var increments = isPowerProgression
                ? new List<string>
                {
                    "明确怪物层级、资源点等级和境界收益的对应关系。",
                    "补充一个限制主角刷怪效率的环境、组织或资源流通规则。",
                    "补充一个卷末打开更高地图的战力门槛。"
                }
                : new List<string>
                {
                    FirstNonEmpty(constitution?.WorldCoreRule, "明确一个能制造后果的世界核心限制。"),
                    "补充一个限制主角行动的制度、组织或资源流通规则。",
                    "补充一个卷末可追踪的隐藏后果。"
                };
            increments.AddRange(knowledge?.GenrePrinciples.Take(2) ?? Enumerable.Empty<string>());
            return increments
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
        }

        private static List<string> BuildMustAvoid(
            VolumeArcPlanningRequest request,
            StoryCreativeConstitution? constitution,
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            var avoid = new List<string>
            {
                "禁止卷中只靠临时新设定解决危机。",
                "禁止连续章节复用同一种胜利结构。",
                "禁止卷末高潮没有代价或状态变化。"
            };
            avoid.AddRange(request.ForbiddenDirections);
            avoid.AddRange(constitution?.ForbiddenDirections ?? Enumerable.Empty<string>());
            avoid.AddRange(knowledge?.TropeWarnings.Take(3) ?? Enumerable.Empty<string>());
            return avoid
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }

        private static string BuildMidpointTurn(
            StoryCreativeConstitution? constitution,
            CreativeKnowledgeRetrievalResult? knowledge,
            bool isPowerProgression)
        {
            if (isPowerProgression)
                return "中段让主角拿到阶段收益，但立刻暴露更高层怪物、资源门槛或敌对势力。";
            if ((constitution?.GenreProfile?.MysteryStrength ?? 0) >= 7)
                return "中段揭示主角追查的目标本身被误导，但前文至少有一个可回看的公平证据。";
            if ((constitution?.GenreProfile?.PleasureStrength ?? 0) >= 8)
                return "中段胜利转化为更高层敌人或限制的注意，爽点之后立刻产生新压力。";
            return knowledge?.AntiTropeStrategies.FirstOrDefault()
                   ?? "中段把外部冲突转化为角色选择困境。";
        }

        private static string BuildClimax(
            StoryCreativeConstitution? constitution,
            string genre,
            bool isPowerProgression)
        {
            if (isPowerProgression)
                return "卷末高潮让主角通过战斗选择、资源兑现和境界突破完成阶段胜利，并打开更高层地图。";
            if ((constitution?.GenreProfile?.EmotionStrength ?? 0) >= 8)
                return "卷末高潮必须让主角在正确答案和重要关系之间做不可逆选择。";
            if (genre.Contains("悬疑") || (constitution?.GenreProfile?.MysteryStrength ?? 0) >= 8)
                return "卷末高潮回收核心证据，同时打开更高层真相。";
            return "卷末高潮让主角以主动代价完成阶段胜利，并把冲突升级到下一卷。";
        }

        private static string BuildBriefText(VolumeArcPlanningRequest request, StoryCreativeConstitution? constitution) =>
            string.Join(" ", new[]
            {
                request.UserGoal,
                request.VolumeTitle,
                string.Join(" ", request.CandidateDirections),
                string.Join(" ", request.ForbiddenDirections),
                constitution?.Genre,
                constitution?.SubGenre,
                constitution?.ReaderPromise,
                constitution?.MainPleasure,
                constitution?.MainConflictEngine,
                constitution?.ProtagonistEngine
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

        private static string BuildDefaultVolumeTitle(string brief, bool isPowerProgression)
        {
            if (isPowerProgression)
                return "怪潮资源与境界突破";
            if (ContainsAny(brief, "关系", "师徒", "盟友", "阵营"))
                return "立场重组与主线推进";
            if (ContainsAny(brief, "悬疑", "调查", "真相", "线索"))
                return "线索压力与目标改写";
            return "主线推进与状态跃迁";
        }

        private static List<string> ExtractForbiddenDirections(VolumeArcPlanningRequest request)
        {
            var forbidden = new List<string>();
            forbidden.AddRange(request.ForbiddenDirections.Where(x => !string.IsNullOrWhiteSpace(x)));
            foreach (var part in request.UserGoal.Split(new[] { '，', '。', '；', ';', '\n', ',', '、' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var text = part.Trim();
                if (text.StartsWith("不要", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("不想要", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("排除", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("禁止", StringComparison.OrdinalIgnoreCase))
                    forbidden.Add(CleanDirection(text));
            }
            return forbidden
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsForbidden(string text, IReadOnlyCollection<string> forbidden) =>
            forbidden.Any(f => !string.IsNullOrWhiteSpace(f) && text.Contains(f, StringComparison.OrdinalIgnoreCase));

        private static string CleanDirection(string direction)
        {
            var clean = direction.Trim();
            clean = clean
                .Replace("不想要", "", StringComparison.OrdinalIgnoreCase)
                .Replace("不要", "", StringComparison.OrdinalIgnoreCase)
                .Replace("排除", "", StringComparison.OrdinalIgnoreCase)
                .Replace("禁止", "", StringComparison.OrdinalIgnoreCase)
                .Trim(' ', '，', '。', '；', ';', ',');
            return clean;
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token) && text.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            return string.Empty;
        }
    }
}

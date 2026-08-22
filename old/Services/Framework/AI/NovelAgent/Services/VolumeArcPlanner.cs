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
            var forbidden = ExtractForbiddenDirections(safeRequest);
            var candidateDirections = CleanDirections(safeRequest.CandidateDirections, forbidden);
            var explicitTitle = safeRequest.VolumeTitle.Trim();
            var title = string.IsNullOrWhiteSpace(explicitTitle) || IsGenericVolumeTitle(explicitTitle)
                ? BuildVolumeTitle(volumeId, candidateDirections)
                : explicitTitle;
            var expectedCount = Math.Clamp(safeRequest.ExpectedChapterCount, 6, 40);
            var genre = constitution?.Genre ?? string.Empty;
            var promise = FirstNonEmpty(
                constitution?.ReaderPromise,
                BuildVolumePromise(candidateDirections),
                constitution?.MainPleasure,
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
                ExitState = "本卷结束时必须改变至少一个长期变量：身份、阵营、能力位置、世界规则认知或关系结构。",
                CoreQuestion = $"本卷如何把“{BuildCoreFocus(candidateDirections, promise)}”推进成必须承接的阶段结果？",
                MainConflictUpgrade = $"将主线冲突从局部目标升级为卷级压力：{conflict}",
                MidpointReversal = BuildMidpointTurn(constitution, knowledge),
                Climax = BuildClimax(constitution, genre),
                AftermathHook = "卷末胜利必须留下新债务、新规则或新敌我边界，作为下一卷开口。",
                ChapterBeats = BuildBeats(expectedCount, promise, conflict, candidateDirections, forbidden),
                ForeshadowingPlan = BuildForeshadowing(expectedCount, candidateDirections, forbidden),
                CharacterArcPlan = BuildCharacterArcs(constitution),
                WorldbuildingIncrements = BuildWorldbuildingIncrements(constitution, knowledge),
                MustAvoid = BuildMustAvoid(safeRequest, constitution, knowledge)
            };

            return plan;
        }

        private static List<VolumeChapterBeat> BuildBeats(
            int expectedCount,
            string promise,
            string conflict,
            IReadOnlyCollection<string> candidateDirections,
            IReadOnlyCollection<string> forbidden)
        {
            var roles = BuildBeatRoles(candidateDirections, forbidden);

            var beats = new List<VolumeChapterBeat>();
            for (var i = 1; i <= expectedCount; i++)
            {
                var ratio = expectedCount == 1 ? 1 : (double)(i - 1) / (expectedCount - 1);
                var role = roles[Math.Min(roles.Count - 1, (int)Math.Round(ratio * (roles.Count - 1)))];
                beats.Add(new VolumeChapterBeat
                {
                    Index = i,
                    Role = role,
                    Goal = BuildBeatGoal(role, promise),
                    Turn = BuildBeatTurn(role, conflict),
                    Cost = BuildBeatCost(role)
                });
            }

            return beats;
        }

        private static List<string> BuildBeatRoles(
            IReadOnlyCollection<string> candidateDirections,
            IReadOnlyCollection<string> forbidden)
        {
            return candidateDirections
                .Where(x => !string.IsNullOrWhiteSpace(x) && !IsForbidden(x, forbidden))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .DefaultIfEmpty("目标推进")
                .ToList();
        }

        private static string BuildBeatGoal(string role, string promise) =>
            $"围绕“{role}”推进一个可追踪的冲突变量，并兑现本卷承诺：{promise}";

        private static string BuildBeatTurn(string role, string conflict) =>
            $"让“{role}”带来的信息、关系、资源或环境认知发生变化，并继续压向主冲突：{conflict}";

        private static string BuildBeatCost(string role) =>
            $"“{role}”至少产生一个后续章节可追踪的小后果。";

        private static List<VolumeForeshadowPlan> BuildForeshadowing(
            int expectedCount,
            IReadOnlyCollection<string> candidateDirections,
            IReadOnlyCollection<string> forbidden)
        {
            var plans = candidateDirections
                .Where(x => !string.IsNullOrWhiteSpace(x) && !IsForbidden(x, forbidden))
                .Take(5)
                .Select((direction, index) => new VolumeForeshadowPlan
                {
                    Name = CleanDirection(direction),
                    Setup = $"围绕“{CleanDirection(direction)}”提前放一个可追踪细节。",
                    Payoff = $"在本卷中后段兑现“{CleanDirection(direction)}”带来的行动变化。",
                    PayoffBeatIndex = Math.Max(2, Math.Min(expectedCount, expectedCount / 2 + index + 1))
                })
                .ToList();

            return plans
                .Where(plan => !IsForbidden($"{plan.Name} {plan.Setup} {plan.Payoff}", forbidden))
                .GroupBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(5)
                .ToList();
        }

        private static List<VolumeCharacterArc> BuildCharacterArcs(StoryCreativeConstitution? constitution)
        {
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
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            var increments = new List<string>
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
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            if ((constitution?.GenreProfile?.MysteryStrength ?? 0) >= 7)
                return "中段揭示主角追查的目标本身被误导，但前文至少有一个可回看的公平证据。";
            if ((constitution?.GenreProfile?.PleasureStrength ?? 0) >= 8)
                return "中段胜利转化为更高层敌人或限制的注意，爽点之后立刻产生新压力。";
            return knowledge?.AntiTropeStrategies.FirstOrDefault()
                   ?? "中段把外部冲突转化为角色选择困境。";
        }

        private static string BuildClimax(
            StoryCreativeConstitution? constitution,
            string genre)
        {
            if ((constitution?.GenreProfile?.EmotionStrength ?? 0) >= 8)
                return "卷末高潮必须让主角在正确答案和重要关系之间做不可逆选择。";
            if (genre.Contains("悬疑") || (constitution?.GenreProfile?.MysteryStrength ?? 0) >= 8)
                return "卷末高潮回收核心证据，同时打开更高层真相。";
            return "卷末高潮让主角以主动代价完成阶段胜利，并把冲突升级到下一卷。";
        }

        private static string BuildDefaultVolumeTitle(IReadOnlyCollection<string> candidateDirections)
        {
            var firstDirection = candidateDirections.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstDirection))
                return firstDirection.Trim();
            return "主线推进与状态跃迁";
        }

        private static string BuildVolumeTitle(string volumeId, IReadOnlyCollection<string> candidateDirections)
        {
            var prefix = BuildVolumeDisplayName(volumeId);
            var core = BuildDefaultVolumeTitle(candidateDirections);
            return string.IsNullOrWhiteSpace(prefix) ? core : $"{prefix}：{core}";
        }

        private static string BuildVolumeDisplayName(string volumeId)
        {
            var digits = new string(volumeId.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var number) && number > 0)
                return $"第{ToChineseNumber(number)}卷";
            return string.IsNullOrWhiteSpace(volumeId) ? "第一卷" : volumeId.Trim();
        }

        private static string ToChineseNumber(int number) =>
            number switch
            {
                1 => "一",
                2 => "二",
                3 => "三",
                4 => "四",
                5 => "五",
                6 => "六",
                7 => "七",
                8 => "八",
                9 => "九",
                10 => "十",
                _ => number.ToString()
            };

        private static bool IsGenericVolumeTitle(string title)
        {
            var clean = title.Trim().Trim('：', ':', ' ', '\t');
            if (clean.Length == 0)
                return true;
            if (clean is "第一卷" or "第1卷" or "卷一" or "卷1")
                return true;
            return clean.StartsWith("第", StringComparison.OrdinalIgnoreCase) &&
                   clean.EndsWith("卷", StringComparison.OrdinalIgnoreCase) &&
                   clean.Length <= 5;
        }

        private static string BuildVolumePromise(IReadOnlyCollection<string> candidateDirections)
        {
            var directions = candidateDirections
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(3)
                .ToList();
            if (directions.Count == 0)
                return string.Empty;
            return $"围绕“{string.Join("、", directions)}”推进本卷目标，形成可追踪的阶段胜利和后续压力。";
        }

        private static string BuildCoreFocus(IReadOnlyCollection<string> candidateDirections, string fallback)
        {
            var directions = candidateDirections
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(2)
                .ToList();
            if (directions.Count > 0)
                return string.Join("、", directions);
            return fallback;
        }

        private static List<string> ExtractForbiddenDirections(VolumeArcPlanningRequest request)
        {
            var forbidden = new List<string>();
            forbidden.AddRange(request.ForbiddenDirections.Where(x => !string.IsNullOrWhiteSpace(x)));
            return forbidden
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> CleanDirections(
            IEnumerable<string> directions,
            IReadOnlyCollection<string> forbidden) =>
            directions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(CleanDirection)
                .Where(x => !string.IsNullOrWhiteSpace(x) && !IsForbidden(x, forbidden))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static bool IsForbidden(string text, IReadOnlyCollection<string> forbidden) =>
            forbidden.Any(f => !string.IsNullOrWhiteSpace(f) && text.Contains(f, StringComparison.OrdinalIgnoreCase));

        private static string CleanDirection(string direction)
        {
            var clean = ExtractDirectionTitle(direction);
            clean = clean
                .Replace("不想要", "", StringComparison.OrdinalIgnoreCase)
                .Replace("不要", "", StringComparison.OrdinalIgnoreCase)
                .Replace("排除", "", StringComparison.OrdinalIgnoreCase)
                .Replace("禁止", "", StringComparison.OrdinalIgnoreCase)
                .Trim(' ', '，', '。', '；', ';', ',');
            return clean;
        }

        private static string ExtractDirectionTitle(string direction)
        {
            var title = direction.Trim();
            var open = title.IndexOf('【');
            var close = open >= 0 ? title.IndexOf('】', open + 1) : -1;
            if (open >= 0 && close > open)
            {
                var inner = title[(open + 1)..close].Trim();
                var separator = inner.IndexOfAny(new[] { '：', ':' });
                if (separator >= 0 && separator + 1 < inner.Length)
                    inner = inner[(separator + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(inner))
                    return inner;
            }

            var firstSentenceEnd = title.IndexOfAny(new[] { '。', '；', ';', '\n' });
            if (firstSentenceEnd > 0)
                title = title[..firstSentenceEnd].Trim();

            var colon = title.IndexOfAny(new[] { '：', ':' });
            if (colon >= 0 && colon + 1 < title.Length && title[..colon].Contains("方向", StringComparison.OrdinalIgnoreCase))
                title = title[(colon + 1)..].Trim();

            return title;
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

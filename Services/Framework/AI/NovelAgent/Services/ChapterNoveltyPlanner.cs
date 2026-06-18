using System;
using System.Collections.Generic;
using System.Linq;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class ChapterNoveltyPlanner
    {
        public ChapterCreativeBrief BuildBrief(ChapterCreativeRequest request)
        {
            var chapterId = string.IsNullOrWhiteSpace(request.ChapterId) ? "next" : request.ChapterId.Trim();
            var constitution = request.Constitution ?? new StoryCreativeConstitution();
            var forbidden = new List<string>
            {
                "反派降智",
                "无代价开挂",
                "关键人物刚好路过救场",
                "重复上一章的冲突解决方式"
            };
            forbidden.AddRange(constitution.ForbiddenDirections);

            var candidates = ScoreCandidates(GenerateCandidates(request, constitution), request, constitution);
            var best = candidates
                .OrderByDescending(c => c.TotalScore)
                .First();
            var knowledge = request.CreativeKnowledge;

            return new ChapterCreativeBrief
            {
                ChapterId = chapterId,
                CoreIdea = best.CoreTwist,
                ConflictMove = best.ConflictMove,
                CharacterChoice = best.CharacterChoice,
                CostOrConsequence = best.CostOrConsequence,
                ForeshadowingAction = request.ActiveForeshadowing.Count > 0
                    ? $"选择一个活跃伏笔推进或误导：{request.ActiveForeshadowing[0]}"
                    : "若无活跃伏笔，本章至少埋下一个可追踪的小伏笔。",
                WorldbuildingGap = "若写作必须新增设定，先标记为 Proposed，不得直接覆盖 Canon。",
                ForbiddenPatterns = forbidden.Distinct().ToList(),
                KnowledgeNotes = BuildKnowledgeNotes(knowledge)
                    .Concat(new CommercialRhythmChecker().BuildPlanningNotes(constitution))
                    .Distinct()
                    .Take(12)
                    .ToList(),
                AntiTropeStrategies = knowledge?.AntiTropeStrategies.Take(5).ToList() ?? new List<string>(),
                PatternWarnings = BuildPatternWarnings(knowledge),
                SimilarContentWarnings = BuildSimilarContentWarnings(request),
                VolumeArcNotes = BuildVolumeArcNotes(request),
                VolumeBeatRole = request.VolumeBeat?.Role ?? string.Empty,
                RecommendedCandidateTitle = best.Title,
                RecommendationReason = best.RecommendationReason,
                SelectedCandidateTitle = best.Title,
                SelectionMode = "AgentRecommended",
                SelectionRationale = "默认采用 Agent 推荐候选；正式生成前仍建议用户确认或改选。",
                Candidates = candidates
            };
        }

        private static List<PlotCandidate> GenerateCandidates(ChapterCreativeRequest request, StoryCreativeConstitution constitution)
        {
            var goal = string.IsNullOrWhiteSpace(request.UserGoal) ? "推进当前章节目标" : request.UserGoal.Trim();
            var conflict = request.ActiveConflicts.FirstOrDefault() ?? "当前主线冲突";
            var characterState = request.CharacterStates.FirstOrDefault() ?? "主角当前目标、秘密、关系或心理压力";
            var forbidden = ExtractForbiddenDirections(request, constitution);
            var candidates = new List<PlotCandidate>();

            var signalText = BuildSignalText(request, constitution);
            var profile = constitution.GenreProfile ?? new GenreDirectionProfile();
            var directions = BuildCandidateDirections(request, constitution, signalText, profile);
            candidates.AddRange(directions
                .Where(direction => !IsForbidden(direction, forbidden))
                .Select(direction => BuildDirectionalCandidate(direction, request, goal, conflict, characterState, constitution)));

            var filtered = candidates
                .Where(candidate => !IsForbidden($"{candidate.Title} {candidate.CoreTwist} {candidate.ConflictMove} {candidate.CharacterChoice} {candidate.CostOrConsequence}", forbidden))
                .GroupBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (filtered.Count == 0)
                filtered.Add(BuildGenericCandidate(goal, conflict, characterState));

            return filtered;
        }

        private static List<string> BuildCandidateDirections(
            ChapterCreativeRequest request,
            StoryCreativeConstitution constitution,
            string signalText,
            GenreDirectionProfile profile)
        {
            var directions = request.CandidateDirections
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            if (ContainsAny(signalText, "打怪", "升级", "刷怪", "突破", "怪潮", "碾压"))
            {
                directions.Add("连续战斗升级");
                directions.Add("资源点争夺成长");
            }

            if (ContainsAny(signalText, "失败", "挫败", "失手", "误判", "调查", "线索") || profile.DepthStrength >= 8)
                directions.Add("挫败后获得关键线索");

            if (ContainsAny(signalText, "代价", "交换", "承诺", "债务", "牺牲", "亏损", "风险")
                || ContainsAny(constitution.ProtagonistEngine + " " + constitution.ReaderPromise, "代价", "债务"))
                directions.Add("付出承诺换取阶段进展");

            if (ContainsAny(signalText, "认知", "反转", "误导", "真相", "谜", "悬疑", "调查") || profile.MysteryStrength >= 8)
                directions.Add("目标认知被新证据改写");

            if (ContainsAny(signalText, "关系", "师徒", "盟友", "女角色", "情感", "立场", "阵营", "背叛")
                || profile.EmotionStrength >= 8
                || profile.EnsembleStrength >= 8
                || HasKnowledgeSignal(request.CreativeKnowledge, "关系", "情绪", "盟友", "阵营"))
                directions.Add("人物立场重组推进主线");

            if (request.ActiveForeshadowing.Count > 0 || ContainsAny(signalText, "伏笔", "回收", "埋线", "线索"))
                directions.Add("旧细节改变当前行动");

            if (ContainsAny(signalText, "规则", "反噬", "污染", "限制", "约束") || profile.WorldbuildingStrength >= 9)
                directions.Add("世界限制转化为新压力");

            if (directions.Count == 0)
                directions.Add("目标推进并留下新压力");

            return directions
                .Select(CleanDirection)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
        }

        private static PlotCandidate BuildDirectionalCandidate(
            string direction,
            ChapterCreativeRequest request,
            string goal,
            string conflict,
            string characterState,
            StoryCreativeConstitution constitution)
        {
            if (ContainsAny(direction, "战斗", "打怪", "升级", "突破", "刷怪", "怪潮", "碾压"))
                return BuildPowerProgressionCandidate(direction, goal, conflict, characterState);
            if (ContainsAny(direction, "资源", "争夺", "夺宝", "收益"))
                return BuildResourceCandidate(direction, goal, conflict);
            if (ContainsAny(direction, "线索", "挫败", "失败", "失手", "误判"))
                return BuildEvidenceAfterSetbackCandidate(direction, goal, conflict, characterState);
            if (ContainsAny(direction, "承诺", "代价", "债务", "牺牲", "风险"))
                return BuildConsequenceCandidate(direction, goal, conflict);
            if (ContainsAny(direction, "认知", "证据", "真相", "误导", "调查", "谜"))
                return BuildEvidenceReframeCandidate(direction, goal, conflict);
            if (ContainsAny(direction, "人物", "关系", "立场", "阵营", "师徒", "盟友", "女角色"))
                return BuildRelationshipCandidate(direction, goal, conflict, characterState);
            if (ContainsAny(direction, "旧细节", "伏笔", "埋线", "回收"))
                return BuildOldDetailCandidate(request, direction, goal, conflict);
            if (ContainsAny(direction, "世界", "限制", "规则", "压力", "污染", "约束"))
                return BuildWorldPressureCandidate(direction, goal, conflict, constitution);

            return BuildGenericCandidate(goal, conflict, characterState, direction);
        }

        private static PlotCandidate BuildGenericCandidate(string goal, string conflict, string characterState) => new()
        {
            Title = "目标推进",
            CoreTwist = $"主角围绕“{goal}”完成一次可见推进，并让局势进入下一阶段。",
            ConflictMove = $"{conflict} 至少升级一个压力变量。",
            CharacterChoice = $"主角基于“{characterState}”做出主动选择。",
            CostOrConsequence = "获得进展，同时留下下一章必须处理的新压力。",
            NoveltyScore = 6,
            ConsistencyScore = 8,
            DramaScore = 7,
            TypeMatchScore = 7,
            ClicheRisk = 2,
            Risks = new List<string> { "兜底候选必须继续由用户方向细化。" }
        };

        private static PlotCandidate BuildGenericCandidate(string goal, string conflict, string characterState, string direction) => new()
        {
            Title = EnsureTitle(direction),
            CoreTwist = $"主角围绕“{goal}”按“{direction}”完成一次可见推进，并让局势进入下一阶段。",
            ConflictMove = $"{conflict} 至少升级一个压力变量。",
            CharacterChoice = $"主角基于“{characterState}”做出主动选择。",
            CostOrConsequence = "获得进展，同时留下下一章必须处理的新压力。",
            NoveltyScore = 6,
            ConsistencyScore = 8,
            DramaScore = 7,
            TypeMatchScore = 7,
            ClicheRisk = 2,
            Risks = new List<string> { "候选方向需要在生成正文前继续具体化为场景、行动和后果。" }
        };

        private static PlotCandidate BuildPowerProgressionCandidate(string direction, string goal, string conflict, string characterState) => new()
        {
            Title = EnsureTitle(direction),
            CoreTwist = $"主角通过连续战斗推进“{goal}”，每一场战斗都带来可见资源或境界收益。",
            ConflictMove = $"{conflict} 从单点威胁升级为更高层怪物、资源点或敌对势力压力。",
            CharacterChoice = $"主角基于“{characterState}”选择稳扎稳打发育，而不是冒进开挂。",
            CostOrConsequence = "消耗体力、资源或暴露部分实力，但换来明确成长台阶。",
            NoveltyScore = 7,
            ConsistencyScore = 9,
            DramaScore = 8,
            TypeMatchScore = 9,
            ClicheRisk = 2,
            Risks = new List<string> { "需要让每场战斗改变资源、境界或地图状态，避免流水账刷怪。" }
        };

        private static PlotCandidate BuildResourceCandidate(string direction, string goal, string conflict) => new()
        {
            Title = EnsureTitle(direction),
            CoreTwist = $"推进“{goal}”的关键变成争夺一处能让主角升级的资源点。",
            ConflictMove = $"{conflict} 引出敌人、怪物和女角色阵营的多方抢夺。",
            CharacterChoice = "主角选择低调布局，先拿收益再处理暴露风险。",
            CostOrConsequence = "拿到升级资源，但引来下一波更强对手。",
            NoveltyScore = 8,
            ConsistencyScore = 8,
            DramaScore = 8,
            TypeMatchScore = 9,
            ClicheRisk = 2,
            Risks = new List<string> { "资源收益必须具体，不能只写获得宝物却不改变战力结构。" }
        };

        private static PlotCandidate BuildEvidenceAfterSetbackCandidate(string direction, string goal, string conflict, string characterState) => new()
        {
            Title = EnsureTitle(direction),
            CoreTwist = $"主角没有完成“{goal}”，但失败暴露了更关键的线索。",
            ConflictMove = $"{conflict} 从表层对抗升级为信息不对称。",
            CharacterChoice = $"主角基于“{characterState}”选择承认短期失败，换取长期布局空间。",
            CostOrConsequence = "失去资源、信任或行动窗口中的至少一项。",
            NoveltyScore = 8,
            ConsistencyScore = 8,
            DramaScore = 8,
            TypeMatchScore = 7,
            ClicheRisk = 2,
            Risks = new List<string> { "若失败只停留在口头挫折，会削弱推进感。" }
        };

        private static PlotCandidate BuildConsequenceCandidate(string direction, string goal, string conflict) => new()
        {
            Title = EnsureTitle(direction),
            CoreTwist = $"主角能推进“{goal}”，但必须交换一个未来会产生后果的承诺。",
            ConflictMove = $"{conflict} 引入新的约束条件。",
            CharacterChoice = "主角主动接受不完美胜利。",
            CostOrConsequence = "新增承诺、秘密、债务或关系裂痕。",
            NoveltyScore = 7,
            ConsistencyScore = 9,
            DramaScore = 8,
            TypeMatchScore = 8,
            ClicheRisk = 3,
            Risks = new List<string> { "承诺或债务必须能在后续章节回收，否则会变成空设定。" }
        };

        private static PlotCandidate BuildEvidenceReframeCandidate(string direction, string goal, string conflict) => new()
        {
            Title = EnsureTitle(direction),
            CoreTwist = $"读者以为本章在解决“{goal}”，实际揭示目标本身被误导。",
            ConflictMove = $"{conflict} 的敌我边界发生变化。",
            CharacterChoice = "主角选择相信一个不完整但危险的新解释。",
            CostOrConsequence = "旧计划作废，下一章必须处理认知代价。",
            NoveltyScore = 9,
            ConsistencyScore = 7,
            DramaScore = 9,
            TypeMatchScore = 8,
            ClicheRisk = 2,
            Risks = new List<string> { "反转必须提前埋证据，不能靠临时新规则硬拧。" }
        };

        private static PlotCandidate BuildRelationshipCandidate(string direction, string goal, string conflict, string characterState) => new()
        {
            Title = EnsureTitle(direction),
            CoreTwist = $"推进“{goal}”的关键不在力量，而在一段关系的破裂或重组。",
            ConflictMove = $"{conflict} 从外部事件转为人物立场冲突。",
            CharacterChoice = $"主角必须在正确答案和“{characterState}”之间选择。",
            CostOrConsequence = "一个盟友、师长或对手的态度发生可追踪变化。",
            NoveltyScore = 7,
            ConsistencyScore = 8,
            DramaScore = 9,
            TypeMatchScore = 7,
            ClicheRisk = 3,
            Risks = new List<string> { "关系变化需要有可追踪后果，避免情绪戏脱离主线。" }
        };

        private static PlotCandidate BuildOldDetailCandidate(ChapterCreativeRequest request, string direction, string goal, string conflict) => new()
        {
            Title = EnsureTitle(direction),
            CoreTwist = request.ActiveForeshadowing.Count > 0
                ? $"本章表面推进“{goal}”，实际回收或扭转伏笔“{request.ActiveForeshadowing[0]}”。"
                : $"本章推进“{goal}”时埋下一个会在三章内兑现的小伏笔。",
            ConflictMove = $"{conflict} 被一个旧细节重新解释，冲突压力前移。",
            CharacterChoice = "主角选择相信细节而不是相信表面胜利。",
            CostOrConsequence = "旧信息被推翻，主角必须承担误判带来的行动成本。",
            NoveltyScore = 8,
            ConsistencyScore = 8,
            DramaScore = 7,
            TypeMatchScore = 8,
            ClicheRisk = 2,
            Risks = new List<string> { "伏笔回收不能只靠解释，最好推动一个当场选择。" }
        };

        private static PlotCandidate BuildWorldPressureCandidate(string direction, string goal, string conflict, StoryCreativeConstitution constitution) => new()
        {
            Title = EnsureTitle(direction),
            CoreTwist = $"主角推进“{goal}”时，世界限制或既有规则把胜利转化为新的行动压力。",
            ConflictMove = $"{conflict} 从人物对抗升级为世界规则、制度或资源限制压力。",
            CharacterChoice = "主角必须在短期收益和长期约束之间选择。",
            CostOrConsequence = FirstNonEmpty(constitution.WorldCoreRule, "新增一个必须被后续章节处理的限制、污染或债务。"),
            NoveltyScore = 8,
            ConsistencyScore = 7,
            DramaScore = 8,
            TypeMatchScore = 9,
            ClicheRisk = 3,
            Risks = new List<string> { "新增限制必须进入 Canon Ledger 或章节后果，避免设定漂移。" }
        };

        private static string BuildSignalText(ChapterCreativeRequest request, StoryCreativeConstitution constitution) =>
            string.Join(" ", new[]
            {
                request.UserGoal,
                constitution.Genre,
                constitution.SubGenre,
                constitution.ReaderPromise,
                constitution.MainPleasure,
                constitution.SecondaryPleasure,
                constitution.MainConflictEngine,
                constitution.ProtagonistEngine,
                constitution.NoveltyPoint,
                string.Join(" ", request.ActiveConflicts),
                string.Join(" ", request.ActiveForeshadowing),
                string.Join(" ", request.CharacterStates),
                string.Join(" ", request.UsedPlotPatterns),
                string.Join(" ", constitution.ForbiddenDirections),
                string.Join(" ", request.CreativeKnowledge?.GenrePrinciples ?? Enumerable.Empty<string>()),
                string.Join(" ", request.CreativeKnowledge?.AntiTropeStrategies ?? Enumerable.Empty<string>()),
                string.Join(" ", request.CreativeKnowledge?.ProjectMemory ?? Enumerable.Empty<string>())
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

        private static bool HasKnowledgeSignal(CreativeKnowledgeRetrievalResult? knowledge, params string[] tokens)
        {
            if (knowledge == null)
                return false;
            var text = string.Join(" ", knowledge.GenrePrinciples
                .Concat(knowledge.AntiTropeStrategies)
                .Concat(knowledge.TropeWarnings)
                .Concat(knowledge.ProjectMemory));
            return ContainsAny(text, tokens);
        }

        private static List<string> ExtractForbiddenDirections(ChapterCreativeRequest request, StoryCreativeConstitution constitution)
        {
            var forbidden = new List<string>();
            forbidden.AddRange(constitution.ForbiddenDirections.Where(x => !string.IsNullOrWhiteSpace(x)));
            forbidden.AddRange(request.ForbiddenDirections.Where(x => !string.IsNullOrWhiteSpace(x)));
            forbidden.AddRange(request.UsedPlotPatterns.Where(x => !string.IsNullOrWhiteSpace(x) && x.Contains("不要", StringComparison.OrdinalIgnoreCase)));

            var parts = request.UserGoal.Split(new[] { '，', '。', '；', ';', '\n', ',', '、' }, StringSplitOptions.RemoveEmptyEntries);
            forbidden.AddRange(parts
                .Select(p => p.Trim())
                .Where(p => p.StartsWith("不要", StringComparison.OrdinalIgnoreCase)
                            || p.StartsWith("不想要", StringComparison.OrdinalIgnoreCase)
                            || p.StartsWith("排除", StringComparison.OrdinalIgnoreCase)
                            || p.StartsWith("禁止", StringComparison.OrdinalIgnoreCase))
                .Select(p => p
                    .Replace("不想要", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("不要", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("排除", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("禁止", "", StringComparison.OrdinalIgnoreCase)
                    .Trim()));

            return forbidden
                .SelectMany(ExpandForbidden)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> ExpandForbidden(string value)
        {
            var text = value.Trim();
            if (string.IsNullOrWhiteSpace(text))
                yield break;
            yield return text;
            yield return text.Replace("型", "", StringComparison.OrdinalIgnoreCase);
            yield return text.Replace("流", "", StringComparison.OrdinalIgnoreCase);
            if (text.Contains("规则", StringComparison.OrdinalIgnoreCase) && text.Contains("反噬", StringComparison.OrdinalIgnoreCase))
                yield return "规则反噬";
            if (text.Contains("认知", StringComparison.OrdinalIgnoreCase) && text.Contains("反转", StringComparison.OrdinalIgnoreCase))
                yield return "认知反转";
            if (text.Contains("关系", StringComparison.OrdinalIgnoreCase) && (text.Contains("破局", StringComparison.OrdinalIgnoreCase) || text.Contains("代价", StringComparison.OrdinalIgnoreCase)))
                yield return "关系";
        }

        private static bool IsForbidden(string text, IReadOnlyCollection<string> forbidden) =>
            forbidden.Any(f => !string.IsNullOrWhiteSpace(f) && text.Contains(f, StringComparison.OrdinalIgnoreCase));

        private static bool ContainsAny(string text, params string[] tokens)
        {
            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token) && text.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static List<PlotCandidate> ScoreCandidates(
            List<PlotCandidate> candidates,
            ChapterCreativeRequest request,
            StoryCreativeConstitution constitution)
        {
            foreach (var candidate in candidates)
            {
                var typeBonus = CalculateTypeBonus(candidate, constitution);
                var repetitionPenalty = request.UsedPlotPatterns.Any(p =>
                    !string.IsNullOrWhiteSpace(p)
                    && (candidate.Title.Contains(p) || candidate.CoreTwist.Contains(p) || candidate.ConflictMove.Contains(p)))
                    ? 3
                    : 0;
                var foreshadowBonus = request.ActiveForeshadowing.Count > 0
                    && candidate.Title.Contains("伏笔")
                        ? 2
                        : 0;
                var knowledgeBonus = CalculateKnowledgeBonus(candidate, request.CreativeKnowledge);
                var knowledgePenalty = CalculateKnowledgePenalty(candidate, request.CreativeKnowledge);
                var similarContentPenalty = CalculateSimilarContentPenalty(candidate, request.SimilarContentFragments);
                var volumeBeatBonus = CalculateVolumeBeatBonus(candidate, request.VolumeBeat);

                candidate.TypeMatchScore = ClampScore(candidate.TypeMatchScore + typeBonus);
                candidate.TotalScore =
                    candidate.NoveltyScore * 3
                    + candidate.ConsistencyScore * 3
                    + candidate.DramaScore * 2
                    + candidate.TypeMatchScore * 2
                    + foreshadowBonus
                    + knowledgeBonus
                    + volumeBeatBonus
                    - candidate.ClicheRisk * 2
                    - repetitionPenalty
                    - knowledgePenalty
                    - similarContentPenalty;

                ApplyKnowledgeSupport(candidate, request.CreativeKnowledge);
                candidate.RecommendationReason = BuildRecommendationReason(
                    candidate,
                    typeBonus,
                    foreshadowBonus,
                    repetitionPenalty,
                    knowledgeBonus,
                    knowledgePenalty,
                    similarContentPenalty,
                    volumeBeatBonus);

                if (repetitionPenalty > 0)
                    candidate.Risks.Add("命中已用桥段模式，需要改写表达方式或剧情功能。");
                if (knowledgePenalty > 0)
                    candidate.Risks.Add("创意知识库提示存在套路或项目重复风险，生成正文前需要采用反套路方案。");
                if (similarContentPenalty > 0)
                    candidate.Risks.Add("RAG 检索命中相似正文片段，需要改变信息来源、场景压力或角色代价。");
            }

            return candidates
                .OrderByDescending(c => c.TotalScore)
                .ToList();
        }

        private static int CalculateTypeBonus(PlotCandidate candidate, StoryCreativeConstitution constitution)
        {
            var profile = constitution.GenreProfile ?? new GenreDirectionProfile();
            var title = candidate.Title;
            var bonus = 0;

            if (profile.PleasureStrength >= 8 && (title.Contains("代价") || title.Contains("规则")))
                bonus += 1;
            if (profile.MysteryStrength >= 8 && (title.Contains("认知") || title.Contains("伏笔")))
                bonus += 2;
            if (profile.EmotionStrength >= 8 && title.Contains("关系"))
                bonus += 2;
            if (profile.EnsembleStrength >= 8 && title.Contains("关系"))
                bonus += 1;
            if (profile.WorldbuildingStrength >= 8 && title.Contains("规则"))
                bonus += 2;
            if (profile.DepthStrength >= 8 && (title.Contains("失败") || title.Contains("认知")))
                bonus += 1;

            return bonus;
        }

        private static string BuildRecommendationReason(
            PlotCandidate candidate,
            int typeBonus,
            int foreshadowBonus,
            int repetitionPenalty,
            int knowledgeBonus,
            int knowledgePenalty,
            int similarContentPenalty,
            int volumeBeatBonus)
        {
            var reasons = new List<string>
            {
                $"综合分 {candidate.TotalScore}",
                $"新鲜度 {candidate.NoveltyScore}",
                $"一致性 {candidate.ConsistencyScore}",
                $"戏剧张力 {candidate.DramaScore}",
                $"类型匹配 {candidate.TypeMatchScore}",
                $"套路风险 {candidate.ClicheRisk}"
            };

            if (typeBonus > 0)
                reasons.Add($"命中当前类型风向 +{typeBonus}");
            if (foreshadowBonus > 0)
                reasons.Add($"优先处理活跃伏笔 +{foreshadowBonus}");
            if (repetitionPenalty > 0)
                reasons.Add($"疑似重复旧桥段 -{repetitionPenalty}");
            if (knowledgeBonus > 0)
                reasons.Add($"创意知识支持 +{knowledgeBonus}");
            if (knowledgePenalty > 0)
                reasons.Add($"知识库套路风险 -{knowledgePenalty}");
            if (similarContentPenalty > 0)
                reasons.Add($"相似正文风险 -{similarContentPenalty}");
            if (volumeBeatBonus > 0)
                reasons.Add($"贴合卷级节拍 +{volumeBeatBonus}");

            return string.Join("；", reasons);
        }

        private static int CalculateKnowledgeBonus(
            PlotCandidate candidate,
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            if (knowledge == null || knowledge.Hits.Count == 0) return 0;

            var bonus = 0;
            if (knowledge.GenrePrinciples.Any(k => HasTokenOverlap(k, candidate.Title)
                                                   || HasTokenOverlap(k, candidate.CoreTwist)
                                                   || HasTokenOverlap(k, candidate.ConflictMove)))
                bonus += 2;
            if (knowledge.AntiTropeStrategies.Count > 0)
                bonus += 1;
            if (knowledge.EmotionRelationshipGuides.Any(k => HasTokenOverlap(k, candidate.Title)
                                                              || HasTokenOverlap(k, candidate.CharacterChoice)
                                                              || HasTokenOverlap(k, candidate.CostOrConsequence)))
                bonus += candidate.Title.Contains("关系") ? 3 : 1;
            if (knowledge.Hits.Any(h => h.Entry.Category == CreativeKnowledgeCategory.ReaderPromise
                                        && HasTokenOverlap(h.Entry.Content, candidate.Title + candidate.CoreTwist)))
                bonus += 1;

            return bonus;
        }

        private static int CalculateKnowledgePenalty(
            PlotCandidate candidate,
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            if (knowledge == null) return 0;

            var text = $"{candidate.Title} {candidate.CoreTwist} {candidate.ConflictMove} {candidate.CharacterChoice}";
            var tropeHits = knowledge.Hits.Count(h =>
                h.Entry.Category is CreativeKnowledgeCategory.TropePattern or CreativeKnowledgeCategory.ProjectUsedPattern
                && HasTokenOverlap(text, h.Entry.Content + " " + h.Entry.Title));

            return tropeHits == 0 ? 0 : Math.Min(4, tropeHits * 2);
        }

        private static int CalculateSimilarContentPenalty(
            PlotCandidate candidate,
            IReadOnlyCollection<string> similarContentFragments)
        {
            if (similarContentFragments == null || similarContentFragments.Count == 0)
                return 0;

            var candidateText = $"{candidate.Title} {candidate.CoreTwist} {candidate.ConflictMove} {candidate.CharacterChoice} {candidate.CostOrConsequence}";
            var hitCount = similarContentFragments.Count(fragment => HasTokenOverlap(fragment, candidateText));
            return hitCount == 0 ? 0 : Math.Min(5, hitCount * 2);
        }

        private static int CalculateVolumeBeatBonus(
            PlotCandidate candidate,
            VolumeChapterBeat? beat)
        {
            if (beat == null) return 0;

            var text = $"{candidate.Title} {candidate.CoreTwist} {candidate.ConflictMove} {candidate.CharacterChoice} {candidate.CostOrConsequence}";
            var beatText = $"{beat.Role} {beat.Goal} {beat.Turn} {beat.Cost}";
            var bonus = HasTokenOverlap(text, beatText) ? 2 : 0;

            if (beat.Role.Contains("反转") && candidate.Title.Contains("认知"))
                bonus += 2;
            if (beat.Role.Contains("代价") && candidate.Title.Contains("代价"))
                bonus += 2;
            if (beat.Role.Contains("伏笔") && candidate.Title.Contains("伏笔"))
                bonus += 2;
            if (beat.Role.Contains("高潮") && (candidate.Title.Contains("规则") || candidate.Title.Contains("关系")))
                bonus += 1;
            if (beat.Role.Contains("失败") && candidate.Title.Contains("失败"))
                bonus += 2;

            return Math.Min(5, bonus);
        }

        private static void ApplyKnowledgeSupport(
            PlotCandidate candidate,
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            if (knowledge == null) return;

            candidate.KnowledgeSupport = knowledge.Hits
                .Where(h => HasTokenOverlap(
                    $"{candidate.Title} {candidate.CoreTwist} {candidate.ConflictMove}",
                    $"{h.Entry.Title} {h.Entry.Content} {string.Join(" ", h.Entry.Tags)}"))
                .OrderByDescending(h => h.Score)
                .Take(3)
                .Select(h => $"{h.Entry.Title}：{h.Entry.Content}")
                .ToList();

            candidate.AntiTropePlan = knowledge.AntiTropeStrategies.FirstOrDefault(strategy =>
                HasTokenOverlap(strategy, $"{candidate.Title} {candidate.CoreTwist} {candidate.ConflictMove}"))
                ?? knowledge.AntiTropeStrategies.FirstOrDefault()
                ?? string.Empty;
        }

        private static List<string> BuildKnowledgeNotes(CreativeKnowledgeRetrievalResult? knowledge)
        {
            if (knowledge == null) return new List<string>();
            return knowledge.GenrePrinciples
                .Concat(knowledge.EmotionRelationshipGuides)
                .Concat(knowledge.ProjectMemory)
                .Take(8)
                .Distinct()
                .ToList();
        }

        private static List<string> BuildPatternWarnings(CreativeKnowledgeRetrievalResult? knowledge)
        {
            if (knowledge == null) return new List<string>();
            return knowledge.TropeWarnings
                .Concat(knowledge.ProjectMemory)
                .Take(8)
                .Distinct()
                .ToList();
        }

        private static List<string> BuildSimilarContentWarnings(ChapterCreativeRequest request)
        {
            if (request.SimilarContentFragments.Count == 0)
                return new List<string>();

            return request.SimilarContentFragments
                .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
                .Take(6)
                .Select(fragment => $"避免复用相似正文功能：{fragment}")
                .ToList();
        }

        private static List<string> BuildVolumeArcNotes(ChapterCreativeRequest request)
        {
            var notes = new List<string>();
            if (request.VolumeArc != null)
            {
                notes.Add($"卷目标：{request.VolumeArc.VolumePromise}");
                notes.Add($"卷核心问题：{request.VolumeArc.CoreQuestion}");
                notes.Add($"卷中反转：{request.VolumeArc.MidpointReversal}");
                notes.Add($"卷末高潮：{request.VolumeArc.Climax}");
                notes.AddRange(request.VolumeArc.WorldbuildingIncrements
                    .Take(3)
                    .Select(item => $"世界观增量：{item}"));
            }

            if (request.VolumeBeat != null)
            {
                notes.Add($"本章卷级节拍：{request.VolumeBeat.Role} / {request.VolumeBeat.Goal}");
                notes.Add($"本章节拍转折：{request.VolumeBeat.Turn}");
                notes.Add($"本章节拍代价：{request.VolumeBeat.Cost}");
            }

            return notes
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .Distinct()
                .Take(10)
                .ToList();
        }

        private static bool HasTokenOverlap(string content, string expected)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(expected))
                return false;

            var normalizedExpected = expected.Trim();
            if (normalizedExpected.Length <= 8)
                return content.Contains(normalizedExpected, System.StringComparison.OrdinalIgnoreCase);

            var tokens = normalizedExpected
                .Split(new[] { ' ', '\t', '\r', '\n', '，', '。', '、', '；', ';', ',', '.', '：', ':', '！', '？', '(', ')', '（', '）', '/', '|' },
                    System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                .Where(t => t.Length >= 2)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            if (tokens.Count == 0)
                return content.Contains(normalizedExpected, System.StringComparison.OrdinalIgnoreCase);

            var hitCount = tokens.Count(t => content.Contains(t, System.StringComparison.OrdinalIgnoreCase));
            return hitCount >= System.Math.Max(1, System.Math.Min(3, tokens.Count / 2));
        }

        private static int ClampScore(int value)
        {
            if (value < 1) return 1;
            if (value > 10) return 10;
            return value;
        }

        private static string CleanDirection(string direction)
        {
            var clean = direction.Trim();
            clean = clean
                .Replace("这些都不要", "", StringComparison.OrdinalIgnoreCase)
                .Replace("就要", "", StringComparison.OrdinalIgnoreCase)
                .Replace("不要", "", StringComparison.OrdinalIgnoreCase)
                .Replace("禁止", "", StringComparison.OrdinalIgnoreCase)
                .Replace("排除", "", StringComparison.OrdinalIgnoreCase)
                .Trim(' ', '，', '。', '；', ';', ',');
            return clean;
        }

        private static string EnsureTitle(string direction)
        {
            var clean = CleanDirection(direction);
            return string.IsNullOrWhiteSpace(clean) ? "目标推进" : clean;
        }

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    }
}

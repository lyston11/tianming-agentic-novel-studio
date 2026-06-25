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
            var knowledge = request.CreativeKnowledge;
            var best = candidates.FirstOrDefault();

            return new ChapterCreativeBrief
            {
                ChapterId = chapterId,
                CoreIdea = best?.CoreTwist ?? string.Empty,
                ConflictMove = best?.ConflictMove ?? string.Empty,
                CharacterChoice = best?.CharacterChoice ?? string.Empty,
                CostOrConsequence = best?.CostOrConsequence ?? string.Empty,
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
                RecommendedCandidateTitle = best?.Title ?? string.Empty,
                RecommendationReason = best?.RecommendationReason ?? "需要 Agent 先提供结构化候选方向。",
                SelectedCandidateTitle = best?.Title ?? string.Empty,
                SelectionMode = best == null ? "NeedsAgentCreativeDirections" : "AgentRecommended",
                SelectionRationale = best == null
                    ? "章节规划工具不会从用户自然语言里推断候选；请由 Agent 先给出 candidateDirections。"
                    : "默认采用 Agent 推荐候选；正式生成前仍建议用户确认或改选。",
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

            var directions = BuildCandidateDirections(request);
            candidates.AddRange(directions
                .Where(direction => !IsForbidden(direction, forbidden))
                .Select(direction => BuildDirectionalCandidate(direction, request, goal, conflict, characterState, constitution)));

            var filtered = candidates
                .Where(candidate => !IsForbidden($"{candidate.Title} {candidate.CoreTwist} {candidate.ConflictMove} {candidate.CharacterChoice} {candidate.CostOrConsequence}", forbidden))
                .GroupBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (filtered.Count == 0 && directions.Count > 1)
            {
                filtered = directions
                    .Select(direction => BuildDirectionalCandidate(direction, request, goal, conflict, characterState, constitution))
                    .GroupBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                foreach (var candidate in filtered)
                {
                    candidate.Risks.Add("禁用方向词面过滤会移除全部候选；已保留候选，但生成正文时必须严格规避禁区并接受硬门禁校验。");
                }
            }

            return filtered;
        }

        private static List<string> BuildCandidateDirections(ChapterCreativeRequest request)
        {
            return request.CandidateDirections
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
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
            return new PlotCandidate
            {
                Title = EnsureTitle(direction),
                CoreTwist = $"主角围绕“{goal}”按“{direction}”完成一次具体场景推进，并让局势进入下一阶段。",
                ConflictMove = $"{conflict} 因“{direction}”发生可追踪变化，不能停留在原地解释。",
                CharacterChoice = $"主角基于“{characterState}”做出主动选择。",
                CostOrConsequence = FirstNonEmpty(
                    constitution.WorldCoreRule,
                    "获得进展，同时留下下一章必须处理的新压力。"),
                NoveltyScore = 7,
                ConsistencyScore = 8,
                DramaScore = 8,
                TypeMatchScore = 7,
                ClicheRisk = 2,
                Risks = new List<string> { "候选方向需要在生成正文前继续具体化为场景、行动和后果。" }
            };
        }

        private static List<string> ExtractForbiddenDirections(ChapterCreativeRequest request, StoryCreativeConstitution constitution)
        {
            var forbidden = new List<string>();
            forbidden.AddRange(constitution.ForbiddenDirections.Where(x => !string.IsNullOrWhiteSpace(x)));
            forbidden.AddRange(request.ForbiddenDirections.Where(x => !string.IsNullOrWhiteSpace(x)));

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
                .Concat(knowledge.HardFacts)
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

using System.Collections.Generic;
using System.Linq;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class BookConceptDesigner
    {
        private readonly GenreDirectionPlanner _genreDirectionPlanner;

        public BookConceptDesigner(GenreDirectionPlanner genreDirectionPlanner)
        {
            _genreDirectionPlanner = genreDirectionPlanner;
        }

        public StoryCreativeConstitution BuildConstitution(StoryFoundationRequest request)
        {
            var profile = _genreDirectionPlanner.BuildProfile(request.Genre, request.SubGenre);
            var forbidden = ExtractForbiddenDirections(request);
            var seed = CleanSeedForPositiveBrief(request.UserSeed, forbidden);
            var genre = string.IsNullOrWhiteSpace(request.Genre) ? "未定题材" : request.Genre.Trim();
            var direction = request.CandidateDirections.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim()
                            ?? profile.Strategy;

            return new StoryCreativeConstitution
            {
                Genre = genre,
                SubGenre = request.SubGenre?.Trim() ?? string.Empty,
                ReaderPromise = $"围绕“{seed}”持续兑现 {genre} 读者期待：{direction}",
                CoreHook = $"主角面对一个会不断升级认知难度的核心困境：{seed}",
                CoreTheme = "人在既定规则与自我选择之间，如何付出代价仍然改变命运。",
                MainPleasure = DescribeMainPleasure(profile),
                SecondaryPleasure = DescribeSecondaryPleasure(profile),
                WorldCoreRule = "世界规则必须能产生冲突、代价和选择，不能只是背景介绍。",
                MainConflictEngine = "主角目标、世界规则、对手利益三者持续互相挤压，每卷至少升级一次冲突层级。",
                ProtagonistEngine = "主角的外在目标必须被内在缺陷阻碍，成长来自选择代价，而不是无条件变强。",
                NoveltyPoint = "每个关键胜利都带来新的约束或认知反转，避免单纯重复升级。",
                DepthLayer = "用类型快感承载更深层的价值冲突，而不是把主题写成说教。",
                ForbiddenDirections = BuildForbiddenDirections(profile),
                CommercialRhythm = "开篇强钩子，三章内建立核心困境；每卷中段升级代价，卷末给出认知反转或重大状态变化。",
                GenreProfile = profile
            };
        }

        public IReadOnlyList<MacroStoryConceptCandidate> GenerateMacroCandidates(StoryFoundationRequest request)
        {
            var constitution = BuildConstitution(request);
            var forbidden = ExtractForbiddenDirections(request);
            var directions = BuildCandidateDirections(request);

            var candidates = directions
                .Where(direction => !IsForbidden(direction, forbidden))
                .Select((direction, index) => BuildCandidate(direction, index + 1, request, constitution))
                .Where(candidate => !IsForbidden($"{candidate.Title} {candidate.WorldCoreRule} {candidate.MainConflictEngine}", forbidden))
                .GroupBy(candidate => NormalizeKey(candidate.Title), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(c => c.NoveltyScore + c.SustainabilityScore + c.TypeMatchScore)
                .ToList();

            return candidates
                .OrderByDescending(c => c.NoveltyScore + c.SustainabilityScore + c.TypeMatchScore)
                .Take(4)
                .ToList();
        }

        private static List<string> BuildCandidateDirections(StoryFoundationRequest request) =>
            request.CandidateDirections
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static MacroStoryConceptCandidate BuildCandidate(
            string direction,
            int index,
            StoryFoundationRequest request,
            StoryCreativeConstitution constitution)
        {
            var cleanDirection = CleanDirection(direction);
            var title = EnsureCandidateTitle(cleanDirection);
            var forbidden = ExtractForbiddenDirections(request);
            var seed = CleanSeedForPositiveBrief(request.UserSeed, forbidden);
            var profile = constitution.GenreProfile ?? new GenreDirectionProfile();

            return new MacroStoryConceptCandidate
            {
                CandidateId = $"macro-{index:000}-{Slug(cleanDirection)}",
                Title = title,
                CoreHook = $"围绕“{seed}”，按“{cleanDirection}”组织整书主线和阶段性兑现。",
                WorldCoreRule = $"世界规则服务于“{cleanDirection}”：每卷必须让主角获得新的能力、位置或局势变量。",
                MainConflictEngine = $"主角目标、外部阻力和“{cleanDirection}”的类型快感持续碰撞，每卷完成一次状态跃迁。",
                ProtagonistEngine = constitution.ProtagonistEngine,
                WorldbuildingBlueprint = BuildWorldbuildingBlueprint(seed, cleanDirection),
                ProgressionSystem = BuildProgressionSystem(cleanDirection),
                ProtagonistProfile = BuildProtagonistProfile(seed, cleanDirection),
                PleasureLoop = BuildPleasureLoop(cleanDirection),
                FirstThreeVolumes = BuildFirstThreeVolumes(cleanDirection),
                KeyCharacters = BuildKeyCharacters(cleanDirection),
                DepthLayer = constitution.DepthLayer,
                NoveltyScore = ClampScore(7 + (profile.WorldbuildingStrength >= 8 ? 1 : 0)),
                SustainabilityScore = 8,
                TypeMatchScore = ClampScore(7 + (profile.PleasureStrength >= 8 ? 1 : 0)),
                Risks =
                {
                    "需要把方向拆成卷级状态变化，否则会停留在概念口号。",
                    "必须持续检查用户明确排除的方向，不能把已拒绝结构换名塞回候选。"
                }
            };
        }

        private static string BuildWorldbuildingBlueprint(string seed, string direction)
        {
            return $"围绕“{seed}”建立能持续制造选择压力的世界结构；每卷揭示一个新规则、一组新势力和一个不可回避的长期后果。";
        }

        private static string BuildProgressionSystem(string direction)
        {
            return $"成长链条围绕「{direction}」展开：发现限制 -> 做出选择 -> 获得新能力或位置 -> 引出更高层冲突。";
        }

        private static string BuildProtagonistProfile(string seed, string direction)
        {
            return $"主角被“{seed}”牵引进入主线，优势与缺陷都必须服务「{direction}」，每卷完成一次认知或关系位置变化。";
        }

        private static string BuildPleasureLoop(string direction)
        {
            return $"核心循环：目标受阻 -> 利用「{direction}」破局 -> 获得状态变化 -> 暴露新压力 -> 推进下一阶段选择。";
        }

        private static List<string> BuildFirstThreeVolumes(string direction)
        {
            return new List<string>
            {
                $"第一卷：建立「{direction}」的核心困境和主角第一次状态变化。",
                "第二卷：扩大势力、规则或关系压力，让旧解法失效。",
                "第三卷：完成一次卷级反转或身份跃迁，打开整书更大目标。"
            };
        }

        private static List<string> BuildKeyCharacters(string direction)
        {
            return new List<string>
            {
                "主角：承担核心选择和成长线。",
                "关键盟友：提供不同价值观和行动资源。",
                "主要对手：持续制造卷级压力。",
                "灰色中间人：连接资源、情报和风险。",
                "高阶压迫者：作为前三卷之后的大目标。"
            };
        }

        private static List<string> ExtractForbiddenDirections(StoryFoundationRequest request)
        {
            var forbidden = new List<string>();
            forbidden.AddRange(request.ForbiddenDirections.Where(x => !string.IsNullOrWhiteSpace(x)));

            return forbidden
                .SelectMany(ExpandForbidden)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string CleanSeedForPositiveBrief(string seed, IReadOnlyCollection<string> forbidden)
        {
            if (string.IsNullOrWhiteSpace(seed))
                return "一个尚未命名的长篇故事";

            var clean = CleanPositiveCreativeText(seed, forbidden);
            return string.IsNullOrWhiteSpace(clean) ? "一个尚未命名的长篇故事" : clean;
        }

        private static bool StartsAsNegativeConstraint(string text) =>
            text.StartsWith("不要", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("不想要", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("排除", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("禁止", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("别", StringComparison.OrdinalIgnoreCase);

        private static string CleanOperationalInstruction(string text)
        {
            var clean = text.Trim();
            clean = clean.Replace("新写一本小说，", "", StringComparison.OrdinalIgnoreCase)
                .Replace("新写一本小说", "", StringComparison.OrdinalIgnoreCase)
                .Replace("新开一本小说，", "", StringComparison.OrdinalIgnoreCase)
                .Replace("新开一本小说", "", StringComparison.OrdinalIgnoreCase)
                .Replace("开一本新小说，", "", StringComparison.OrdinalIgnoreCase)
                .Replace("开一本新小说", "", StringComparison.OrdinalIgnoreCase)
                .Replace("写一本小说，", "", StringComparison.OrdinalIgnoreCase)
                .Replace("写一本小说", "", StringComparison.OrdinalIgnoreCase)
                .Replace("请直接开始执行，", "", StringComparison.OrdinalIgnoreCase)
                .Replace("请直接开始执行", "", StringComparison.OrdinalIgnoreCase)
                .Replace("直接开始执行，", "", StringComparison.OrdinalIgnoreCase)
                .Replace("直接开始执行", "", StringComparison.OrdinalIgnoreCase)
                .Replace("先建立故事地基，", "", StringComparison.OrdinalIgnoreCase)
                .Replace("先建立故事地基", "", StringComparison.OrdinalIgnoreCase)
                .Trim(' ', '，', '。', '；', ';');

            clean = RemoveInstructionClause(clean, "包含");
            clean = RemoveInstructionClause(clean, "包括");
            clean = RemoveInstructionClause(clean, "需要");
            clean = RemoveInstructionClause(clean, "要求");
            clean = RemoveInstructionClause(clean, "要有");

            return clean;
        }

        private static string RemoveInstructionClause(string text, string marker)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return text;

            var prefix = text[..index].Trim(' ', '，', '。', '；', ';');
            if (index == 0)
                return string.Empty;

            return LooksLikeOutputInstruction(text[index..])
                ? prefix
                : text;
        }

        private static bool LooksLikeOutputInstruction(string text) =>
            ContainsAny(text,
                "世界观",
                "升级体系",
                "能力体系",
                "主角",
                "爽点循环",
                "前三卷",
                "首批角色",
                "人物骨架",
                "角色骨架",
                "故事地基");

        private static string CleanPositiveCreativeText(string text, IReadOnlyCollection<string> forbidden)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var parts = text
                .Split(new[] { '。', '；', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Where(p => !StartsAsNegativeConstraint(p))
                .Select(CleanOperationalInstruction)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Where(p => !forbidden.Any(f => IsCompactForbiddenMention(p, f)))
                .ToList();

            var clean = string.Join("。", parts).Trim(' ', '。', '；', ';', '\n');
            return string.IsNullOrWhiteSpace(clean) ? text.Trim() : clean;
        }

        private static bool IsCompactForbiddenMention(string text, string forbidden)
        {
            if (string.IsNullOrWhiteSpace(forbidden))
                return false;

            var normalizedText = NormalizeConstraintText(text);
            var normalizedForbidden = NormalizeConstraintText(forbidden);
            return normalizedForbidden.Length >= 2
                   && normalizedText.Contains(normalizedForbidden, StringComparison.OrdinalIgnoreCase)
                   && (StartsAsNegativeConstraint(text)
                       || normalizedText.Contains("不要", StringComparison.OrdinalIgnoreCase)
                       || normalizedText.Contains("排除", StringComparison.OrdinalIgnoreCase)
                       || normalizedText.Contains("禁止", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeConstraintText(string value)
        {
            var chars = value.Where(char.IsLetterOrDigit).ToArray();
            return chars.Length == 0 ? string.Empty : new string(chars);
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
            if (text.Contains("真相", StringComparison.OrdinalIgnoreCase) && text.Contains("递进", StringComparison.OrdinalIgnoreCase))
                yield return "真相递进";
            if (text.Contains("关系", StringComparison.OrdinalIgnoreCase) && text.Contains("代价", StringComparison.OrdinalIgnoreCase))
                yield return "关系代价";
        }

        private static bool IsForbidden(string text, IReadOnlyCollection<string> forbidden) =>
            forbidden.Any(f => !string.IsNullOrWhiteSpace(f) && text.Contains(f, StringComparison.OrdinalIgnoreCase));

        private static string CleanDirection(string direction)
        {
            var clean = direction.Trim();
            clean = clean.Replace("这些都不要", "", StringComparison.OrdinalIgnoreCase)
                .Replace("就要", "", StringComparison.OrdinalIgnoreCase)
                .Replace("的", "", StringComparison.OrdinalIgnoreCase)
                .Trim(' ', '，', '。', '；', ';', ',');
            return string.IsNullOrWhiteSpace(clean) ? "主线成长" : clean;
        }

        private static string EnsureCandidateTitle(string direction)
        {
            var title = ExtractDirectionTitle(direction);
            if (title.EndsWith("型", StringComparison.OrdinalIgnoreCase) || title.EndsWith("流", StringComparison.OrdinalIgnoreCase))
                return title;
            return title + "流";
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

        private static string NormalizeKey(string value) =>
            value.Replace("型", "", StringComparison.OrdinalIgnoreCase)
                .Replace("流", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

        private static string Slug(string value)
        {
            var chars = value.Where(char.IsLetterOrDigit).Take(18).ToArray();
            return chars.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : new string(chars);
        }

        private static int ClampScore(int value) => Math.Max(1, Math.Min(10, value));

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

        private static string DescribeMainPleasure(GenreDirectionProfile profile)
        {
            if (profile.MysteryStrength >= profile.PleasureStrength && profile.MysteryStrength >= 8)
                return "烧脑递进、信息差、误导与反转。";
            if (profile.PleasureStrength >= 8)
                return "压迫后的反击、成长兑现、资源获取和局势翻盘。";
            if (profile.EmotionStrength >= 8)
                return "关系拉扯、情绪代价和关键选择。";
            return "清晰目标驱动下的持续推进和状态变化。";
        }

        private static string DescribeSecondaryPleasure(GenreDirectionProfile profile)
        {
            var parts = new List<string>();
            if (profile.WorldbuildingStrength >= 7) parts.Add("世界规则探索");
            if (profile.EnsembleStrength >= 7) parts.Add("多方势力博弈");
            if (profile.DepthStrength >= 7) parts.Add("主题深度");
            if (parts.Count == 0) parts.Add("伏笔回收和章节钩子");
            return string.Join("、", parts);
        }

        private static List<string> BuildForbiddenDirections(GenreDirectionProfile profile)
        {
            var list = new List<string>
            {
                "禁止只靠临时新设定解决危机。",
                "禁止章节没有故事变量变化。",
                "禁止角色行为只服务剧情方便而缺少动机。"
            };
            list.AddRange(profile.RiskWarnings);
            return list.Distinct().ToList();
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
    }
}

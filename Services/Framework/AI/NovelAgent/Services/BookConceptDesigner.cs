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
            var profile = _genreDirectionPlanner.BuildProfile(request.Genre, request.SubGenre, request.DesiredDirection);
            var forbidden = ExtractForbiddenDirections(request);
            var seed = CleanSeedForPositiveBrief(request.UserSeed, forbidden);
            var genre = string.IsNullOrWhiteSpace(request.Genre) ? "未定题材" : request.Genre.Trim();
            var direction = string.IsNullOrWhiteSpace(request.DesiredDirection) ? profile.Strategy : request.DesiredDirection.Trim();

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
            var brief = BuildBriefText(request);
            var forbidden = ExtractForbiddenDirections(request);
            var directions = BuildCandidateDirections(request, constitution, forbidden);

            var candidates = directions
                .Where(direction => !IsForbidden(direction, forbidden))
                .Select((direction, index) => BuildCandidate(direction, index + 1, request, constitution))
                .Where(candidate => !IsForbidden($"{candidate.Title} {candidate.CoreHook} {candidate.WorldCoreRule} {candidate.MainConflictEngine}", forbidden))
                .GroupBy(candidate => NormalizeKey(candidate.Title), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(c => c.NoveltyScore + c.SustainabilityScore + c.TypeMatchScore)
                .ToList();

            if (candidates.Count >= 3)
                return candidates.Take(4).ToList();

            foreach (var fallback in BuildFallbackDirections(request, constitution, brief))
            {
                if (IsForbidden(fallback, forbidden))
                    continue;
                var candidate = BuildCandidate(fallback, candidates.Count + 1, request, constitution);
                if (!IsForbidden($"{candidate.Title} {candidate.CoreHook} {candidate.WorldCoreRule} {candidate.MainConflictEngine}", forbidden))
                    candidates.Add(candidate);
                if (candidates.Count >= 3)
                    break;
            }

            return candidates
                .OrderByDescending(c => c.NoveltyScore + c.SustainabilityScore + c.TypeMatchScore)
                .ToList();
        }

        private static string BuildBriefText(StoryFoundationRequest request) =>
            string.Join(" ", new[]
            {
                request.UserSeed,
                request.Genre,
                request.SubGenre,
                request.TargetReader,
                request.DesiredDirection,
                string.Join(" ", request.CandidateDirections),
                string.Join(" ", request.ForbiddenDirections)
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

        private static List<string> BuildCandidateDirections(
            StoryFoundationRequest request,
            StoryCreativeConstitution constitution,
            IReadOnlyCollection<string> forbidden)
        {
            var directions = new List<string>();
            directions.AddRange(request.CandidateDirections.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));

            var brief = BuildBriefText(request);
            if (ContainsAny(brief, "打怪", "刷怪", "升级", "突破", "境界", "副本", "怪潮", "碾压"))
            {
                directions.Add("纯打怪升级变强流");
                directions.Add("资源掠夺成长流");
                directions.Add("怪潮副本推进流");
                directions.Add("境界突破碾压流");
            }

            if (ContainsAny(brief, "经营", "基地", "领地", "势力"))
                directions.Add("基地经营扩张流");
            if (ContainsAny(brief, "后宫", "女角色", "征服", "美女"))
                directions.Add("多女角色征服成长流");
            if (ContainsAny(brief, "末世", "灵气复苏", "灾变"))
                directions.Add("末世资源争夺升级流");
            if (ContainsAny(brief, "学院", "宗门", "试炼"))
                directions.Add("试炼晋升成长流");
            if (ContainsAny(brief, "悬疑", "谜", "真相") && !forbidden.Any(x => x.Contains("真相", StringComparison.OrdinalIgnoreCase)))
                directions.Add("线索破局升级流");

            if (directions.Count == 0)
            {
                directions.Add($"{FirstNonEmpty(request.DesiredDirection, constitution.MainPleasure, "主线推进")}强化型");
                directions.Add($"{FirstNonEmpty(request.Genre, "长篇")}成长兑现型");
                directions.Add($"{FirstNonEmpty(request.UserSeed, "核心钩子")}连续升级型");
            }

            return directions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> BuildFallbackDirections(
            StoryFoundationRequest request,
            StoryCreativeConstitution constitution,
            string brief)
        {
            yield return $"{FirstNonEmpty(request.Genre, "类型")}主线强化流";
            yield return $"{FirstNonEmpty(constitution.MainPleasure, "爽点")}连续兑现流";
            yield return $"{FirstNonEmpty(request.UserSeed, "核心设定")}成长扩张流";
            if (ContainsAny(brief, "打怪", "升级"))
                yield return "打怪升级爽点循环流";
        }

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
            var isPowerFantasy = ContainsAny(cleanDirection, "打怪", "升级", "突破", "境界", "碾压", "资源", "怪潮", "副本", "成长");
            var isEnsemble = ContainsAny(cleanDirection, "后宫", "女角色", "征服", "势力", "基地");
            var isMecha = ContainsAny(seed + " " + cleanDirection + " " + request.Genre + " " + request.SubGenre, "机甲", "战甲", "械", "维修", "矿坑", "废土", "赛博");

            return new MacroStoryConceptCandidate
            {
                CandidateId = $"macro-{index:000}-{Slug(cleanDirection)}",
                Title = title,
                CoreHook = isPowerFantasy
                    ? $"围绕“{seed}”，用打怪、资源、境界突破和更强敌人循环兑现成长爽点。"
                    : $"围绕“{seed}”，按“{cleanDirection}”组织整书主线和阶段性兑现。",
                WorldCoreRule = isPowerFantasy
                    ? "怪物、资源点、境界门槛和敌对势力形成稳定升级阶梯；每次胜利都打开更高层地图和更强目标。"
                    : $"世界规则服务于“{cleanDirection}”：每卷必须让主角获得新的能力、位置或局势变量。",
                MainConflictEngine = isPowerFantasy
                    ? "打怪获取资源，资源推动突破，突破引来更强敌人和更大地图，形成清晰爽点循环。"
                    : $"主角目标、外部阻力和“{cleanDirection}”的类型快感持续碰撞，每卷完成一次状态跃迁。",
                ProtagonistEngine = isPowerFantasy
                    ? "主角靠谨慎发育、系统收益、战斗选择和资源分配持续变强，不靠临时补设定糊弄胜利。"
                    : constitution.ProtagonistEngine,
                WorldbuildingBlueprint = BuildWorldbuildingBlueprint(seed, cleanDirection, isPowerFantasy, isMecha),
                ProgressionSystem = BuildProgressionSystem(seed, cleanDirection, isPowerFantasy, isMecha),
                ProtagonistProfile = BuildProtagonistProfile(seed, cleanDirection, isPowerFantasy, isMecha),
                PleasureLoop = BuildPleasureLoop(cleanDirection, isPowerFantasy, isMecha),
                FirstThreeVolumes = BuildFirstThreeVolumes(seed, cleanDirection, isPowerFantasy, isMecha),
                KeyCharacters = BuildKeyCharacters(seed, cleanDirection, isEnsemble, isMecha),
                DepthLayer = isEnsemble
                    ? "力量成长、欲望选择和阵营归属之间的张力。"
                    : isPowerFantasy
                        ? "生存压力、成长欲望和力量秩序之间的张力。"
                        : constitution.DepthLayer,
                NoveltyScore = ClampScore(7 + (profile.WorldbuildingStrength >= 8 ? 1 : 0) + (isPowerFantasy ? 1 : 0)),
                SustainabilityScore = ClampScore(8 + (isPowerFantasy ? 1 : 0)),
                TypeMatchScore = ClampScore(7 + (profile.PleasureStrength >= 8 ? 1 : 0) + (ContainsAny(BuildBriefText(request), cleanDirection) ? 1 : 0)),
                Risks =
                {
                    isPowerFantasy
                        ? "需要维护怪物阶梯、资源账本和境界收益，否则会退化为重复刷怪。"
                        : "需要把方向拆成卷级状态变化，否则会停留在概念口号。",
                    "必须持续检查用户明确排除的方向，不能把已拒绝结构换名塞回候选。"
                }
            };
        }

        private static string BuildWorldbuildingBlueprint(string seed, string direction, bool isPowerFantasy, bool isMecha)
        {
            if (isMecha)
                return $"废土被矿坑城、地下斗场、企业堡垒和荒野怪潮切成四层秩序；机甲零件、能源炉芯和污染矿脉决定阶级流动。方向「{direction}」要求每个新区域都提供新的怪物材料、改装技术和敌对势力。";
            if (isPowerFantasy)
                return $"围绕“{seed}”建立由怪物等级、资源点、幸存者势力和高阶地图构成的成长世界；每卷至少开放一个新区域和一种新资源。";
            return $"围绕“{seed}”建立能持续制造选择压力的世界结构；每卷揭示一个新规则、一组新势力和一个不可回避的长期后果。";
        }

        private static string BuildProgressionSystem(string seed, string direction, bool isPowerFantasy, bool isMecha)
        {
            if (isMecha)
                return "升级链条：废件修复 -> 模组拼装 -> 能源炉芯觉醒 -> 战甲形态进化 -> 专属武装解锁。材料来自怪物核心、旧时代遗迹和敌方机甲残骸，每次升级都改变战斗方式，而不是只涨数值。";
            if (isPowerFantasy)
                return "成长链条：击败怪物/强敌 -> 获取核心资源 -> 消化或锻造 -> 解锁能力/境界 -> 挑战更高层区域。每一级成长必须配套新的门槛、敌人和地图权限。";
            return $"成长链条围绕「{direction}」展开：发现限制 -> 做出选择 -> 获得新能力或位置 -> 引出更高层冲突。";
        }

        private static string BuildProtagonistProfile(string seed, string direction, bool isPowerFantasy, bool isMecha)
        {
            if (isMecha)
                return "主角出身底层，懂维修、会拆解、能忍耐羞辱；核心优势是把别人眼里的废料看成战力拼图。缺陷是长期被压迫形成的低信任感，成长方向是从独狼维修工变成能掌控战场和队伍的战甲领袖。";
            if (isPowerFantasy)
                return "主角从底层或弱势状态开局，优势是谨慎、狠劲和资源嗅觉；缺陷是过度依赖个人战力。成长方向是从求生升级，逐步掌握队伍、地盘和规则。";
            return $"主角被“{seed}”牵引进入主线，优势与缺陷都必须服务「{direction}」，每卷完成一次认知或关系位置变化。";
        }

        private static string BuildPleasureLoop(string direction, bool isPowerFantasy, bool isMecha)
        {
            if (isMecha)
                return "核心爽点循环：被压迫/被低估 -> 发现可改装材料 -> 修复或升级战甲 -> 在斗场/荒野战斗中打脸 -> 解锁新区域和更强敌人 -> 获得更稀有模块。";
            if (isPowerFantasy)
                return "核心爽点循环：遭遇压迫 -> 打怪夺资源 -> 升级突破 -> 碾压旧敌 -> 引出更强目标 -> 获得新地图或新队友。";
            return $"核心循环：目标受阻 -> 利用「{direction}」破局 -> 获得状态变化 -> 暴露新压力 -> 推进下一阶段选择。";
        }

        private static List<string> BuildFirstThreeVolumes(string seed, string direction, bool isPowerFantasy, bool isMecha)
        {
            if (isMecha)
            {
                return new List<string>
                {
                    "第一卷：矿坑觉醒。主角从奴工/维修工身份切入，修复第一台残破战甲，在地下斗场完成第一次打脸和脱身。",
                    "第二卷：废城猎场。主角进入荒野资源区，猎杀异兽夺取核心模块，建立小队并触碰企业堡垒的资源垄断。",
                    "第三卷：械城夺权。主角带着进化战甲冲击更高层斗场和城邦秩序，击败旧统治者并打开更危险的外层战场。"
                };
            }
            if (isPowerFantasy)
            {
                return new List<string>
                {
                    "第一卷：底层求生与能力觉醒，完成第一次升级闭环。",
                    "第二卷：资源点争夺与队伍扩张，主角从单点变强进入势力竞争。",
                    "第三卷：强敌压境与地图跃迁，用阶段胜利打开更高等级世界。"
                };
            }
            return new List<string>
            {
                $"第一卷：建立「{direction}」的核心困境和主角第一次状态变化。",
                "第二卷：扩大势力、规则或关系压力，让旧解法失效。",
                "第三卷：完成一次卷级反转或身份跃迁，打开整书更大目标。"
            };
        }

        private static List<string> BuildKeyCharacters(string seed, string direction, bool isEnsemble, bool isMecha)
        {
            if (isMecha)
            {
                return new List<string>
                {
                    "主角：底层维修工/矿坑奴工，靠拆解和改装战甲逆袭。",
                    "女机械师：掌握旧时代图纸，嘴硬但尊重真正的技术。",
                    "斗场女王：地下斗场经营者，既是资源入口也是危险盟友。",
                    "企业少主：垄断能源炉芯的对手，代表上层秩序。",
                    "荒野猎人：熟悉怪潮规律，带来外部地图和生存法则。",
                    "旧战甲 AI：残破核心中的战斗辅助，逐步暴露旧时代秘密但不主导主角选择。"
                };
            }
            if (isEnsemble)
            {
                return new List<string>
                {
                    "主角：以成长欲望和生存压力驱动主线。",
                    "强势女角色：提供资源、情绪拉扯或阵营入口。",
                    "竞争型对手：不断逼出主角新能力。",
                    "资源型盟友：帮助主角建立地盘或队伍。",
                    "高阶敌人：代表下一阶段地图门槛。"
                };
            }
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

            foreach (var text in new[] { request.DesiredDirection, request.UserSeed, request.TargetReader })
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var parts = text.Split(new[] { '，', '。', '；', ';', '\n', ',', '、' }, StringSplitOptions.RemoveEmptyEntries);
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
            }

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
            var title = direction.Trim();
            if (title.EndsWith("型", StringComparison.OrdinalIgnoreCase) || title.EndsWith("流", StringComparison.OrdinalIgnoreCase))
                return title;
            return title + "流";
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

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
            var seed = string.IsNullOrWhiteSpace(request.UserSeed) ? "一个尚未命名的长篇故事" : request.UserSeed.Trim();
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
            return new[]
            {
                new MacroStoryConceptCandidate
                {
                    CandidateId = "macro-001-rule-backlash",
                    Title = "规则反噬型",
                    CoreHook = constitution.CoreHook,
                    WorldCoreRule = "主角每次利用世界规则获胜，都会被规则记录并在后续反噬。",
                    MainConflictEngine = "胜利不是终点，而是下一层约束的开始。",
                    ProtagonistEngine = "主角必须在短期收益和长期代价之间做选择。",
                    DepthLayer = "自由选择与系统性代价的冲突。",
                    NoveltyScore = 8,
                    SustainabilityScore = 9,
                    TypeMatchScore = 8,
                    Risks = { "需要持续维护代价账本，否则会退化为普通升级流。" }
                },
                new MacroStoryConceptCandidate
                {
                    CandidateId = "macro-002-truth-ladder",
                    Title = "真相递进型",
                    CoreHook = "每卷揭开一个真相，但新真相会推翻旧解释。",
                    WorldCoreRule = "世界表层规则可被学习，深层规则只能通过代价和失败逼近。",
                    MainConflictEngine = "主角越接近真相，对手越不再只是具体敌人，而是规则本身。",
                    ProtagonistEngine = "主角靠判断力和选择承担风险，而不是单纯靠战力解决问题。",
                    DepthLayer = "认知、信念与现实可塑性的冲突。",
                    NoveltyScore = 9,
                    SustainabilityScore = 8,
                    TypeMatchScore = 8,
                    Risks = { "线索必须公平埋设，避免强行反转。" }
                },
                new MacroStoryConceptCandidate
                {
                    CandidateId = "macro-003-relationship-cost",
                    Title = "关系代价型",
                    CoreHook = "主角每次推进主线，都必须改变一段重要关系。",
                    WorldCoreRule = "力量、信息或资源的获得必须通过关系网络流动。",
                    MainConflictEngine = "外部敌人和内部关系同时制造压力。",
                    ProtagonistEngine = "主角的成长体现在承担关系后果，而不是独自变强。",
                    DepthLayer = "个人命运与关系责任之间的冲突。",
                    NoveltyScore = 7,
                    SustainabilityScore = 8,
                    TypeMatchScore = 7,
                    Risks = { "要避免感情戏脱离主线，关系变化必须影响剧情推进。" }
                }
            }.OrderByDescending(c => c.NoveltyScore + c.SustainabilityScore + c.TypeMatchScore).ToList();
        }

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
    }
}

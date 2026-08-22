using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using Xunit;

namespace Tests.Unit.Services.NovelAgent;

public class PlanningGeneratorTests
{
    [Fact]
    public void BookConceptDesigner_DoesNotInferMacroDirectionsFromUserSeed()
    {
        var designer = new BookConceptDesigner(new GenreDirectionPlanner());

        var candidates = designer.GenerateMacroCandidates(new StoryFoundationRequest
        {
            UserSeed = "写末世打怪升级爽文，主角一路刷怪突破。",
            Genre = "末世升级流"
        });

        Assert.Empty(candidates);
    }

    [Fact]
    public void BookConceptDesigner_DoesNotExtractForbiddenDirectionsFromUserSeed()
    {
        var designer = new BookConceptDesigner(new GenreDirectionPlanner());

        var candidates = designer.GenerateMacroCandidates(new StoryFoundationRequest
        {
            UserSeed = "我要末世升级爽文，不要关系代价型。",
            Genre = "末世升级流",
            CandidateDirections = { "关系代价型" }
        });

        Assert.NotEmpty(candidates);
    }

    [Fact]
    public void ChapterPlanner_DoesNotInferChapterDirectionsFromUserGoal()
    {
        var planner = new ChapterNoveltyPlanner();

        var brief = planner.BuildBrief(new ChapterCreativeRequest
        {
            UserGoal = "这一章要写主角打怪升级，拿到资源点。",
            ActiveConflicts = { "怪潮围城" },
            CharacterStates = { "主角刚拿到银蓝邮徽" }
        });

        Assert.Empty(brief.Candidates);
        Assert.Equal("NeedsAgentCreativeDirections", brief.SelectionMode);
    }

    [Fact]
    public void ChapterPlanner_DoesNotExtractForbiddenDirectionsFromUserGoal()
    {
        var planner = new ChapterNoveltyPlanner();

        var brief = planner.BuildBrief(new ChapterCreativeRequest
        {
            UserGoal = "这一章要打怪升级，不要人物关系破局。",
            CandidateDirections = { "人物关系破局" },
            ActiveConflicts = { "怪潮围城" },
            CharacterStates = { "主角刚拿到银蓝邮徽" }
        });

        Assert.NotEmpty(brief.Candidates);
        Assert.Contains(brief.Candidates, candidate => candidate.Title.Contains("人物关系破局", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VolumeArcPlanner_DoesNotInferVolumeBeatsFromUserGoalOrConstitution()
    {
        var planner = new VolumeArcPlanner();

        var plan = planner.BuildPlan(new VolumeArcPlanningRequest
        {
            UserGoal = "这一卷就写主角一路打怪升级、抢资源点、突破境界。",
            ExpectedChapterCount = 8
        }, new StoryCreativeConstitution
        {
            Genre = "末世打怪升级爽文",
            MainPleasure = "战斗成长、资源获取和碾压强敌。",
            MainConflictEngine = "外部压力持续压迫主角",
            GenreProfile = new GenreDirectionProfile
            {
                PleasureStrength = 9,
                PaceStrength = 9
            }
        }, knowledge: null);

        var planText = string.Join("\n", new[]
        {
            plan.Title,
            plan.ExitState,
            plan.CoreQuestion,
            plan.MidpointReversal,
            plan.Climax,
            string.Join("\n", plan.ChapterBeats.Select(b => $"{b.Role} {b.Goal} {b.Turn} {b.Cost}")),
            string.Join("\n", plan.ForeshadowingPlan.Select(f => $"{f.Name} {f.Setup} {f.Payoff}"))
        });

        Assert.DoesNotContain(plan.ChapterBeats, beat => string.Equals(beat.Role, "怪物压力升级", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.ChapterBeats, beat => string.Equals(beat.Role, "资源点争夺", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.ChapterBeats, beat => string.Equals(beat.Role, "境界突破门槛", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.ForeshadowingPlan, item => string.Equals(item.Name, "高阶怪物痕迹", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("怪潮资源与境界突破", planText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VolumeArcPlanner_DerivesVolumeTitleAndPromiseFromCandidateDirections()
    {
        var planner = new VolumeArcPlanner();

        var plan = planner.BuildPlan(new VolumeArcPlanningRequest
        {
            UserGoal = "规划第一卷。整段创作简报_MARKER：这里包含很多背景、知识、限制和聊天语境，不应该原样塞进卷承诺或核心问题。",
            VolumeId = "volume-001",
            ExpectedChapterCount = 10,
            CandidateDirections =
            {
                "【方向A：废土邮路升级流】主角通过打怪和资源争夺扩张第一条安全邮线。",
                "【方向B：资源点争夺成长流】围绕补给站、断信区和邮路节点推进。",
                "【方向C：邮徽能力边界成长流】邮徽只能识别被篡改邮路，不能攻击或升级。"
            }
        }, new StoryCreativeConstitution
        {
            Genre = "废土邮差打怪升级",
            MainConflictEngine = "无址会持续篡改旧邮路"
        }, knowledge: null);

        Assert.Equal("第一卷：废土邮路升级流", plan.Title);
        Assert.Contains("废土邮路升级流", plan.VolumePromise);
        Assert.Contains("资源点争夺成长流", plan.CoreQuestion);
        Assert.DoesNotContain("整段创作简报_MARKER", plan.VolumePromise, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("整段创作简报_MARKER", plan.CoreQuestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BookConceptDesigner_StripsOperationalInstructionsFromFoundationCandidates()
    {
        var designer = new BookConceptDesigner(new GenreDirectionPlanner());

        var candidates = designer.GenerateMacroCandidates(new StoryFoundationRequest
        {
            UserSeed = "开一本新小说，书名《灰烬机神：从垃圾场维修工到星海战王》，废土末世背景，主角是底层拾荒机修工，通过维修、改装、升级机甲一路逆袭成为万甲之王。核心是打怪升级爽感循环。要有：庞大的废土世界观、机甲升级体系、鲜明主角、爽点循环设计、前三卷规划、首批角色阵容。",
            Genre = "废土机甲打怪升级爽文",
            CandidateDirections = { "纯打怪升级变强流", "机甲改装成长流", "资源争夺成长流" },
            ForbiddenDirections = { "规则反噬", "真相递进", "关系代价" }
        });

        var candidateText = string.Join("\n", candidates.Select(c =>
            $"{c.Title}\n{c.CoreHook}\n{c.WorldbuildingBlueprint}\n{c.ProgressionSystem}\n{c.ProtagonistProfile}\n{c.PleasureLoop}\n{string.Join("\n", c.FirstThreeVolumes)}\n{string.Join("\n", c.KeyCharacters)}"));

        Assert.NotEmpty(candidates);
        Assert.DoesNotContain("请直接开始执行", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("开一本新小说", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("先建立故事地基", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("包含世界观", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("要有", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("首批角色", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("规则反噬", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("真相递进", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("关系代价", candidateText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BookConceptDesigner_DoesNotTreatHardFactConstraintsAsForbiddenDirections()
    {
        var designer = new BookConceptDesigner(new GenreDirectionPlanner());

        var candidates = designer.GenerateMacroCandidates(new StoryFoundationRequest
        {
            UserSeed = "沈砚是雾潮城第七码头的低阶星渊邮差。银蓝邮徽只能辨认被篡改的邮路，不能攻击、治疗或升级。第一卷专注于打怪升级、资源争夺和邮路扩张，避免关系代价型或纯情绪拉扯内容。",
            Genre = "末日废土 / 升级流",
            SubGenre = "邮差奇幻 / 资源争夺 / 能力成长",
            CandidateDirections =
            {
                "方向一【废土邮路升级流】：银蓝邮徽不能攻击、治疗或升级，但沈砚可以通过修复邮路获得资源和行动位置。",
                "方向二【资源点争夺成长流】：成长来自资源积累和邮差装备，不让邮徽本身变强。",
                "方向三【邮徽能力边界成长流】：核心能力不变，主角理解和运用方式不断突破。"
            },
            ForbiddenDirections =
            {
                "银蓝邮徽不能用于攻击、治疗或升级",
                "不能让陆知微沦为工具化花瓶",
                "第一卷不能写关系代价型或纯情绪拉扯内容",
                "不能让沈砚获得超出邮差身份的超能力"
            }
        });

        Assert.NotEmpty(candidates);
        Assert.Contains(candidates, candidate => candidate.Title.Contains("废土邮路升级", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(candidates, candidate => candidate.Title.Contains("关系代价", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ChapterPlanner_RespectsDirectForbiddenDirections()
    {
        var planner = new ChapterNoveltyPlanner();

        var brief = planner.BuildBrief(new ChapterCreativeRequest
        {
            UserGoal = "这一章写主角和女机械师谈判结盟，重点是阵营拉扯。",
            ActiveConflicts = { "斗场怪物压境" },
            CharacterStates = { "主角还是底层维修工" },
            CandidateDirections = { "人物立场重组推进主线" },
            ForbiddenDirections = { "人物立场重组", "关系", "认知改写" },
            Constitution = new StoryCreativeConstitution
            {
                Genre = "废土机甲打怪升级爽文",
                MainPleasure = "打怪、升级、资源获取和碾压强敌。",
                GenreProfile = new GenreDirectionProfile
                {
                    PleasureStrength = 9,
                    PaceStrength = 9,
                    WorldbuildingStrength = 7,
                    Strategy = "用战斗和改装成长兑现爽点。"
                }
            }
        });

        var candidateText = string.Join("\n", brief.Candidates.Select(c => $"{c.Title} {c.CoreTwist} {c.ConflictMove} {c.CharacterChoice}"));

        Assert.Empty(brief.Candidates);
        Assert.Equal("NeedsAgentCreativeDirections", brief.SelectionMode);
        Assert.DoesNotContain("人物立场", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("关系", candidateText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChapterPlanner_DoesNotDropAllBatchCandidatesWhenForbiddenFilterIsOverBroad()
    {
        var planner = new ChapterNoveltyPlanner();

        var brief = planner.BuildBrief(new ChapterCreativeRequest
        {
            UserGoal = "末世邮路第一章，主角沈砚要靠银蓝邮徽辨认旧邮路并打怪成长。",
            ActiveConflicts = { "怪潮逼近废弃邮局" },
            CharacterStates = { "沈砚刚获得银蓝邮徽，但邮徽不能攻击或治愈" },
            CandidateDirections =
            {
                "方向A：沈砚沿旧邮路找到避难所并遭遇第一批变异怪物。",
                "方向B：沈砚被怪物围困，旧邮路信息帮助他逃出生天。",
                "方向C：沈砚发现废弃邮局群，建立第一处资源目标。"
            },
            ForbiddenDirections =
            {
                "邮路"
            }
        });

        Assert.NotEmpty(brief.Candidates);
        Assert.Contains(brief.Candidates, candidate => candidate.Risks.Any(r =>
            r.Contains("禁用方向词面过滤会移除全部候选", StringComparison.OrdinalIgnoreCase)));
    }
}

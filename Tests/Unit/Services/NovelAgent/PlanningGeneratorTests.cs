using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using Xunit;

namespace Tests.Unit.Services.NovelAgent;

public class PlanningGeneratorTests
{
    [Fact]
    public void BookConceptDesigner_StripsOperationalInstructionsFromFoundationCandidates()
    {
        var designer = new BookConceptDesigner(new GenreDirectionPlanner());

        var candidates = designer.GenerateMacroCandidates(new StoryFoundationRequest
        {
            UserSeed = "开一本新小说，书名《灰烬机神：从垃圾场维修工到星海战王》，废土末世背景，主角是底层拾荒机修工，通过维修、改装、升级机甲一路逆袭成为万甲之王。核心是打怪升级爽感循环。要有：庞大的废土世界观、机甲升级体系、鲜明主角、爽点循环设计、前三卷规划、首批角色阵容。",
            Genre = "废土机甲打怪升级爽文",
            DesiredDirection = "纯打怪升级变强流、碾压流、机甲改装成长。要有：庞大的废土世界观、机甲升级体系、鲜明主角、爽点循环设计、前三卷规划、首批角色阵容。",
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

        Assert.DoesNotContain("人物立场", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("关系", candidateText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(brief.Candidates, c =>
            (c.Title + c.CoreTwist + c.ConflictMove).Contains("目标推进", StringComparison.OrdinalIgnoreCase) ||
            (c.Title + c.CoreTwist + c.ConflictMove).Contains("战斗", StringComparison.OrdinalIgnoreCase) ||
            (c.Title + c.CoreTwist + c.ConflictMove).Contains("资源", StringComparison.OrdinalIgnoreCase) ||
            (c.Title + c.CoreTwist + c.ConflictMove).Contains("升级", StringComparison.OrdinalIgnoreCase));
    }
}

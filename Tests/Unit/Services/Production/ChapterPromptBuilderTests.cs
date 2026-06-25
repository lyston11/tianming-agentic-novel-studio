using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterPromptBuilderTests
{
    [Fact]
    public void BuildWritingUserPrompt_IncludesStructuredDirectiveAndChangesSchema()
    {
        IChapterPromptBuilder builder = new ChapterPromptBuilder(new ChapterDirectiveBuilder());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-008",
            UserGoal = "继续写第八章，让主角进入潮汐塔。",
            ChapterBrief = new ChapterCreativeBrief
            {
                RecommendedCandidateTitle = "潮汐塔来信",
                CoreIdea = "沈砚根据银蓝邮徽溯源结果进入潮汐塔"
            }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-008",
            ChapterBlueprints =
            {
                "开章承接第七章结尾的残留信标，推进到潮汐塔外层。",
                "本章结尾留下下一封无法投递的黑信。"
            },
            PreviousSummaries = { "chapter-007: 沈砚确认银蓝邮徽只能辨认邮路和溯源残留信标。" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "主角当前状态：负伤但保持清醒",
                "知识库硬事实：银蓝邮徽只能辨认/溯源邮路，不能攻击或治愈，不能激活机关或新增战斗能力。",
                "下一章必须承接：残留信标指向潮汐塔外层"
            }
        };

        var prompt = builder.BuildWritingUserPrompt(run, context);
        using var doc = JsonDocument.Parse(prompt);

        Assert.Equal(NovelAgentProductionStages.DraftGeneration, doc.RootElement.GetProperty("task").GetString());
        Assert.Equal("chapter-008", doc.RootElement.GetProperty("chapterId").GetString());
        Assert.Contains("CharacterStateChanges", doc.RootElement.GetProperty("requiredChangesSchema").EnumerateArray().Select(x => x.GetString()));

        var directive = doc.RootElement.GetProperty("chapterDirective");
        Assert.Equal("chapter-008", directive.GetProperty("chapterId").GetString());
        Assert.Contains("沈砚", directive.GetProperty("protagonistAnchor").GetString());
        Assert.Contains("残留信标指向潮汐塔外层", directive.GetProperty("mustCarry").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains(directive.GetProperty("knowledgeBoundaries").EnumerateArray().Select(x => x.GetString()),
            item => item != null && item.Contains("银蓝邮徽") && item.Contains("不能攻击"));
    }

    [Fact]
    public void BuildWritingUserPrompt_UsesLlmFactExtractionLabelsAsHardAnchors()
    {
        IChapterPromptBuilder builder = new ChapterPromptBuilder(new ChapterDirectiveBuilder());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            UserGoal = "继续写第二章。"
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            HardContinuityFacts =
            {
                "上一章主角：陈默",
                "上一章主角身份：维修站青年，刚被旧邮路选中",
                "上一章主角状态：左臂受伤但仍能行动",
                "上一章系统状态：银蓝邮徽只能辨认旧邮路，不能攻击",
                "下一章必须承接：陈默必须从堵死的地下室出口脱身"
            }
        };

        var prompt = builder.BuildWritingUserPrompt(run, context);
        using var doc = JsonDocument.Parse(prompt);

        var directive = doc.RootElement.GetProperty("chapterDirective");
        Assert.Contains("姓名=陈默", directive.GetProperty("protagonistAnchor").GetString());
        Assert.Contains("身份=维修站青年", directive.GetProperty("protagonistAnchor").GetString());
        Assert.Contains("当前状态=左臂受伤但仍能行动", directive.GetProperty("protagonistAnchor").GetString());
        Assert.Equal("银蓝邮徽只能辨认旧邮路，不能攻击", directive.GetProperty("systemAnchor").GetString());
        Assert.Contains("陈默必须从堵死的地下室出口脱身", directive.GetProperty("mustCarry").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void BuildWritingUserPrompt_UsesKnowledgeClassificationRuleAsGateBoundary()
    {
        IChapterPromptBuilder builder = new ChapterPromptBuilder(new ChapterDirectiveBuilder());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-003",
            UserGoal = "继续写第三章，保持邮徽能力边界。"
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-003",
            KnowledgeBindings =
            {
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "knowledge-1",
                    Title = "银蓝邮徽能力边界",
                    EntryType = "ReaderPromise",
                    Content = "银蓝邮徽是主线道具。",
                    ConstraintLevel = "HardConstraint",
                    PackagePolicy = "DefaultEveryChapter",
                    ClassificationRule = "银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。",
                    TargetEntities = { "银蓝邮徽" },
                    ShouldEnterGate = true,
                    ShouldEnterBlueprint = true
                }
            }
        };

        var prompt = builder.BuildWritingUserPrompt(run, context);
        using var doc = JsonDocument.Parse(prompt);

        var boundaries = doc.RootElement
            .GetProperty("chapterDirective")
            .GetProperty("knowledgeBoundaries")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToList();
        Assert.Contains(boundaries, item =>
            item != null &&
            item.Contains("银蓝邮徽能力边界", StringComparison.Ordinal) &&
            item.Contains("不能攻击、治愈或升级", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildWritingUserPrompt_UsesKnowledgeClassificationRuleAsBlueprintRequirement()
    {
        IChapterPromptBuilder builder = new ChapterPromptBuilder(new ChapterDirectiveBuilder());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-004",
            UserGoal = "继续写第四章，让邮徽识路成为关键场景。"
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-004",
            KnowledgeBindings =
            {
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "knowledge-blueprint-1",
                    Title = "邮徽识路场景承诺",
                    EntryType = "ReaderPromise",
                    Content = "本章需要展示邮徽参与推进。",
                    ClassificationRule = "本章必须展示邮徽识路但不能攻击。",
                    TargetEntities = { "银蓝邮徽" },
                    ShouldEnterBlueprint = true
                }
            }
        };

        var prompt = builder.BuildWritingUserPrompt(run, context);
        using var doc = JsonDocument.Parse(prompt);

        var sceneBeats = doc.RootElement
            .GetProperty("chapterDirective")
            .GetProperty("sceneBeats")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToList();
        Assert.Contains(sceneBeats, item =>
            item != null &&
            item.Contains("知识蓝图要求：邮徽识路场景承诺", StringComparison.Ordinal) &&
            item.Contains("本章必须展示邮徽识路但不能攻击", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildWritingUserPrompt_PromotesAgentReviewFailuresIntoEditorialRevisionRequirements()
    {
        IChapterPromptBuilder builder = new ChapterPromptBuilder(new ChapterDirectiveBuilder());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            UserGoal = "重写第二章，要打怪升级，不要情绪拉扯。",
            ChapterBrief = new ChapterCreativeBrief
            {
                CoreIdea = "第二章主场景改为怪物围攻，男主靠邮徽辨认逃生路线。"
            }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            Warnings =
            {
                "AgentReview修订要求：核心创意落地=Warning：正文没有明显体现章节核心创意；建议：让打怪升级成为主场景",
                "AgentReview修订要求：章末钩子=Warning：结尾钩子弱；建议：章末留下下一条邮路异常"
            },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "上一章结尾状态：沈砚刚获得银蓝邮徽",
                "AgentReview修订要求：冲突推进=Warning：正文没有改变冲突状态；建议：必须写出怪物围攻并让男主靠邮徽辨认逃生路线"
            }
        };

        var prompt = builder.BuildWritingUserPrompt(run, context);
        using var doc = JsonDocument.Parse(prompt);

        var directive = doc.RootElement.GetProperty("chapterDirective");
        var editorialRequirements = directive
            .GetProperty("editorialRevisionRequirements")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToList();
        Assert.Contains(editorialRequirements, item => item != null && item.Contains("打怪升级") && item.Contains("主场景"));
        Assert.Contains(editorialRequirements, item => item != null && item.Contains("怪物围攻") && item.Contains("邮徽辨认逃生路线"));
        Assert.Contains(editorialRequirements, item => item != null && item.Contains("邮路异常"));
        Assert.Contains(directive.GetProperty("acceptanceRules").EnumerateArray().Select(x => x.GetString()),
            item => item != null && item.Contains("editorialRevisionRequirements") && item.Contains("正文"));
    }

    [Fact]
    public void BuildRepairUserPrompt_IncludesContinuityChecklistAndBoundaryRewritePolicy()
    {
        IChapterPromptBuilder builder = new ChapterPromptBuilder(new ChapterDirectiveBuilder());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-006",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "修订银蓝邮徽越权问题" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-006",
            HardContinuityFacts =
            {
                "下一章必须承接：工会追踪者的持续追捕",
                "下一章必须承接：银蓝邮徽只能辨认/溯源邮路"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = "沈砚发现银蓝邮徽基础共鸣辨识单元获得强化。\n<chapter_changes>{}</chapter_changes>",
            RepairAttemptCount = 2
        };
        var report = new GenerationGateReport
        {
            Issues =
            {
                "核心连续性失败：未承接「工会追踪者的持续追捕」。",
                "知识库硬事实失败：银蓝邮徽能力边界被改写，正文出现「银蓝邮徽基础共鸣/辨识单元获得强化」。"
            },
            RepairHints =
            {
                "开章或关键场景必须回应上一章结尾/必须承接项：工会追踪者的持续追捕",
                "修订正文：银蓝邮徽只能辨认/溯源邮路和残留信标，不能强化或新增能力。"
            }
        };

        var prompt = builder.BuildRepairUserPrompt(run, context, draft, report);
        using var doc = JsonDocument.Parse(prompt);

        Assert.Equal(NovelAgentProductionStages.DraftRepair, doc.RootElement.GetProperty("task").GetString());
        Assert.Contains("工会追踪者的持续追捕", doc.RootElement.GetProperty("requiredContinuityCarryChecklist").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains(doc.RootElement.GetProperty("repairAcceptanceRules").EnumerateArray().Select(x => x.GetString()),
            item => item != null && item.Contains("不要保留") && item.Contains("越权词") && item.Contains("辨认邮路"));

        var policy = doc.RootElement.GetProperty("boundaryRewritePolicy");
        Assert.True(policy.GetProperty("enabled").GetBoolean());
        Assert.Contains("整句改写", policy.GetProperty("instruction").GetString());
        Assert.Contains("基础单元升级", policy.GetProperty("forbiddenPackaging").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void BuildChangesOnlyRepairUserPrompt_StripsDraftChangesAndNeverAsksForFullRewrite()
    {
        IChapterPromptBuilder builder = new ChapterPromptBuilder(new ChapterDirectiveBuilder());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-004",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "承接上一章进入旧画廊" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-004",
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：女子的身份和命运"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第四章正文
            沈砚抵达镜港，发现女子线索指向旧画廊。
            <chapter_changes>
            {"CharacterStateChanges":[{"character":"沈砚"
            """,
            RepairAttemptCount = 2
        };
        var report = new GenerationGateReport
        {
            Issues = { "CHANGES JSON 不是可解析对象。" },
            RepairHints = { "修复 CHANGES JSON 语法，并保留角色、冲突、伏笔等顶级字段。" }
        };

        var prompt = builder.BuildChangesOnlyRepairUserPrompt(run, context, draft, report);
        using var doc = JsonDocument.Parse(prompt);

        Assert.Equal("repair_chapter_changes_only", doc.RootElement.GetProperty("task").GetString());
        Assert.False(doc.RootElement.TryGetProperty("previousDraft", out _));
        Assert.Contains("沈砚抵达镜港", doc.RootElement.GetProperty("chapterBody").GetString());
        Assert.DoesNotContain("chapter_changes", doc.RootElement.GetProperty("chapterBody").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CharacterStateChanges", prompt);
    }
}

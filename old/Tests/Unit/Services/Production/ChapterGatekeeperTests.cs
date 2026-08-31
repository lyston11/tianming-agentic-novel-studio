using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterGatekeeperTests
{
    [Fact]
    public void ApplyHardGates_FailsWhenDraftUsesDifferentProtagonistThanHardContinuity()
    {
        IChapterGatekeeper gatekeeper = new ChapterGatekeeper();
        var report = new GenerationGateReport { Status = "validated" };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "主角当前状态：负伤但清醒",
                "下一章必须承接：银蓝邮徽指向潮汐塔外层"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = "林北推开塔门，完全忘了上一章留下的残留信标。\n<chapter_changes>{}</chapter_changes>"
        };

        gatekeeper.ApplyHardGates(report, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("核心连续性失败") && issue.Contains("沈砚"));
    }

    [Fact]
    public void ApplyHardGates_UsesLlmFactExtractionLabelsForCoreContinuity()
    {
        IChapterGatekeeper gatekeeper = new ChapterGatekeeper();
        var report = new GenerationGateReport { Status = "validated" };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            HardContinuityFacts =
            {
                "上一章主角：陈默",
                "上一章主角状态：左臂受伤但仍能行动",
                "上一章系统状态：银蓝邮徽只能辨认旧邮路，不能攻击",
                "下一章必须承接：陈默必须从堵死的地下室出口脱身"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = "林北打开系统面板，获得火焰冲击后打散雨幕。\n<chapter_changes>{}</chapter_changes>"
        };

        gatekeeper.ApplyHardGates(report, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("硬事实主角") && issue.Contains("陈默"));
        Assert.Contains(report.Issues, issue => issue.Contains("主角当前状态"));
        Assert.Contains(report.Issues, issue => issue.Contains("系统状态"));
        Assert.Contains(report.Issues, issue => issue.Contains("未承接") && issue.Contains("地下室出口"));
    }

    [Fact]
    public void ApplyHardGates_IgnoresTechnicalPreviousChapterMetadataInCarryFacts()
    {
        IChapterGatekeeper gatekeeper = new ChapterGatekeeper();
        var report = new GenerationGateReport { Status = "validated" };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            HardContinuityFacts =
            {
                "下一章必须承接：上一章已提交：第一章：邮徽觉醒**",
                "下一章必须承接：上一章章节ID：project-1-chapter-001",
                "下一章必须承接：银蓝邮徽只能辨认旧邮路，不能攻击"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = "沈砚握紧银蓝邮徽，确认它只能辨认旧邮路，不能攻击追来的蚀骨鼠。\n<chapter_changes>{}</chapter_changes>"
        };

        gatekeeper.ApplyHardGates(report, context, draft);

        Assert.DoesNotContain(report.Issues, issue => issue.Contains("上一章已提交", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Issues, issue => issue.Contains("上一章章节ID", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyHardGates_FailsWhenKnowledgeBoundaryIsExpandedIntoForbiddenFunction()
    {
        IChapterGatekeeper gatekeeper = new ChapterGatekeeper();
        var report = new GenerationGateReport { Status = "validated" };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-006",
            WorldRules =
            {
                "知识库硬事实：银蓝邮徽只能辨认/溯源邮路和残留信标，不能攻击、治愈、激活、腐蚀、锈蚀、干扰或新增战斗能力。"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = "沈砚发现银蓝邮徽基础共鸣辨识单元获得强化，随后干扰了追兵的通讯。\n<chapter_changes>{}</chapter_changes>"
        };

        gatekeeper.ApplyHardGates(report, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("银蓝邮徽") && issue.Contains("能力边界"));
        Assert.False(report.FactSnapshotPassed);
    }

    [Fact]
    public void ApplyHardGates_UsesKnowledgeClassificationRuleAsHardBoundary()
    {
        IChapterGatekeeper gatekeeper = new ChapterGatekeeper();
        var report = new GenerationGateReport { Status = "validated" };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-007",
            KnowledgeBindings =
            {
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "knowledge-1",
                    Title = "银蓝邮徽能力边界",
                    EntryType = "ReaderPromise",
                    Content = "银蓝邮徽是主线道具。",
                    ConstraintLevel = "Reference",
                    PackagePolicy = "RelevantOnly",
                    ClassificationRule = "银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。",
                    TargetEntities = { "银蓝邮徽" },
                    ShouldEnterGate = true
                }
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = "沈砚按住银蓝邮徽，蓝光从掌心铺开，瞬间治愈了他的裂伤。\n<chapter_changes>{}</chapter_changes>"
        };

        gatekeeper.ApplyHardGates(report, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("银蓝邮徽") && issue.Contains("能力边界"));
        Assert.False(report.FactSnapshotPassed);
    }

    [Fact]
    public void ApplyHardGates_FailsWhenChapterAcceptedCreativeIntentIsNotExecuted()
    {
        IChapterGatekeeper gatekeeper = new ChapterGatekeeper();
        var report = new GenerationGateReport { Status = "validated", BlueprintPassed = true };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            AcceptedCreativeIntents =
            {
                new AcceptedCreativeIntentSnapshot
                {
                    IntentId = "intent-1",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-002",
                    NormalizedIntent = "第二章主冲突必须改为怪物围攻，男主通过旧邮徽识别逃生路线，不要恋爱情绪推进。"
                }
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = "第二章里沈砚只是和女主在雨棚下谈心，没有怪物围攻，也没有旧邮徽逃生路线。\n<chapter_changes>{}</chapter_changes>"
        };

        gatekeeper.ApplyHardGates(report, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("已采纳创意未执行") && issue.Contains("怪物围攻"));
        Assert.False(report.BlueprintPassed);
    }

    [Fact]
    public void ApplyHardGates_FailsWhenSourceRevisionPlanRequirementIsNotExecuted()
    {
        IChapterGatekeeper gatekeeper = new ChapterGatekeeper();
        var report = new GenerationGateReport { Status = "validated", BlueprintPassed = true };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            SourceRevisionPlans =
            {
                new RevisionPlanSnapshot
                {
                    RevisionPlanId = "revision-plan-002",
                    PlanType = "chapter_rewrite",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-002",
                    RequirementsJson = "[\"怪物围攻\", \"打怪升级\"]",
                    ContinuityRequirementsJson = "[\"银蓝邮徽不能攻击\"]"
                }
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = "沈砚在维修站里等雨停，只整理了背包，没有怪物围攻，也没有发生打怪升级。\n<chapter_changes>{}</chapter_changes>"
        };

        gatekeeper.ApplyHardGates(report, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("修订计划未执行") && issue.Contains("怪物围攻"));
        Assert.False(report.BlueprintPassed);
    }

    [Fact]
    public void ApplyHardGates_AllowsEquivalentSourceRevisionPlanAbilityBoundary()
    {
        IChapterGatekeeper gatekeeper = new ChapterGatekeeper();
        var report = new GenerationGateReport { Status = "validated", BlueprintPassed = true };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-001",
            SourceRevisionPlans =
            {
                new RevisionPlanSnapshot
                {
                    RevisionPlanId = "revision-plan-001",
                    PlanType = "chapter_rewrite",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-001",
                    RequirementsJson = "[\"怪物围攻\", \"保留沈砚和银蓝邮徽\", \"邮徽不能主动攻击\"]"
                }
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            沈砚在废城邮局醒来时，巡游怪物撞塌投递窗口。他没有把银蓝邮徽当成攻击武器，而是借蓝光看见旧邮路夹缝。
            章末他确认：银蓝邮徽只能指路，不能替他杀敌。
            <chapter_changes>{}</chapter_changes>
            """
        };

        gatekeeper.ApplyHardGates(report, context, draft);

        Assert.DoesNotContain(report.Issues, issue => issue.Contains("修订计划未执行"));
        Assert.True(report.BlueprintPassed);
    }
}

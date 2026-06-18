using Moq;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using Xunit;

namespace Tests.Unit.Services.NovelAgent;

public class HardcoreWritingEngineTests
{
    [Fact]
    public void StripChanges_RemovesXmlChapterChangesBlockFromCommittedContent()
    {
        var engine = new HardcoreWritingEngine(CreateSnapshotService());

        var content = """
        正文第一段。

        正文第二段。
        <chapter_changes>{"NewPlotPoints":["伏笔记录"]}</chapter_changes>
        """;

        var committed = engine.StripChanges(content);

        Assert.Equal("正文第一段。\n\n正文第二段。", committed);
        Assert.DoesNotContain("chapter_changes", committed);
        Assert.DoesNotContain("NewPlotPoints", committed);
    }

    private static StoryStateSnapshotService CreateSnapshotService() =>
        new(
            Mock.Of<IGuideContextService>(),
            Mock.Of<IContentChunkSearchService>(),
            new StoryBibleService());

    [Fact]
    public async Task BuildContextPackageAsync_UsesCommittedPreviousChapterWhenGuidePackageIsMissing()
    {
        var guideContext = new Mock<IGuideContextService>();
        guideContext
            .Setup(x => x.BuildContentContextAsync("chapter-002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TM.Services.Modules.ProjectData.Models.TaskContexts.ContentTaskContext?)null);

        var generatedContent = new Mock<IGeneratedContentService>();
        generatedContent
            .Setup(x => x.GetChapterAsync("chapter-001"))
            .ReturnsAsync("上一章主角在沉船坞遭遇黑潮异兽，修好旧式潜航机甲并首次反杀。");

        var snapshotService = new StoryStateSnapshotService(
            guideContext.Object,
            Mock.Of<IContentChunkSearchService>(),
            new StoryBibleService());

        var engine = new HardcoreWritingEngine(
            snapshotService,
            guideContext.Object,
            generationGate: null!,
            generatedContent.Object,
            contentChunkSearch: Mock.Of<IContentChunkSearchService>(),
            chapterEmbeddingIndex: null,
            chunkEmbeddingIndex: null!,
            embeddingService: null!,
            versionTrackingService: new object(),
            settingsManager: new object());

        var package = await engine.BuildContextPackageAsync(
            new NovelAgentRun
            {
                TargetChapterId = "chapter-002",
                ChapterBrief = new ChapterCreativeBrief
                {
                    ChapterId = "chapter-002",
                    CoreIdea = "主角继续打怪升级"
                }
            },
            new StoryBibleDocument
            {
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "深海废土",
                    SubGenre = "机甲升级",
                    WorldCoreRule = "深海黑潮催生异兽",
                    MainConflictEngine = "猎杀异兽并升级机甲"
                }
            });

        Assert.Contains(package.PreviousSummaries, item => item.Contains("chapter-001"));
        Assert.Contains(package.PreviousSummaries, item => item.Contains("首次反杀"));
        Assert.DoesNotContain(package.Warnings, item => item.Contains("没有可用上章摘要"));
    }

    [Fact]
    public async Task BuildContextPackageAsync_IncludesPreviousContinuityFactsAsHardFacts()
    {
        var engine = new HardcoreWritingEngine(CreateSnapshotService());
        var document = new StoryBibleDocument
        {
            ContinuityFacts =
            {
                new ChapterContinuityFacts
                {
                    ChapterId = "chapter-001",
                    ChapterTitle = "第一章：黑雨维修站",
                    ProtagonistName = "陈默",
                    ProtagonistIdentity = "底层维修工",
                    ProtagonistStatus = "觉醒维修吞噬系统，左臂受伤",
                    CurrentLocation = "城南维修站",
                    SystemState = "维修吞噬系统已激活，能量不足",
                    EquipmentState = "旧扳手、破损护目镜",
                    EndingState = "陈默躲进维修站地下室，黑潮异兽堵住出口",
                    NextChapterMustCarry = { "必须承接地下室被堵出口", "主角姓名必须保持陈默" }
                }
            }
        };

        var package = await engine.BuildContextPackageAsync(
            new NovelAgentRun { TargetChapterId = "chapter-002" },
            document);

        Assert.Contains(package.HardContinuityFacts, item => item.Contains("陈默"));
        Assert.Contains(package.HardContinuityFacts, item => item.Contains("地下室被堵出口"));
        Assert.Contains(package.CharacterStates, item => item.Contains("陈默") && item.Contains("底层维修工"));
    }

    [Fact]
    public async Task ValidateDraftAsync_FailsWhenDraftUsesDifferentProtagonistThanHardContinuity()
    {
        var engine = new HardcoreWritingEngine(CreateSnapshotService());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "承接上一章继续突围" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            WorldRules = { "黑潮异兽会吞噬金属" },
            CharacterStates = { "陈默：底层维修工，左臂受伤" },
            ChapterBlueprints = { "从维修站地下室突围" },
            PreviousSummaries = { "chapter-001: 陈默躲进维修站地下室，黑潮异兽堵住出口。" },
            HardContinuityFacts =
            {
                "主角姓名：陈默",
                "章节结尾状态：陈默躲进维修站地下室，黑潮异兽堵住出口",
                "下一章必须承接：必须承接地下室被堵出口"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第二章：林修醒来
            林修从荒野公路旁醒来，发现自己获得了全新的雷霆系统。
            <chapter_changes>{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("主角") && issue.Contains("陈默"));
    }

    [Fact]
    public async Task ValidateDraftAsync_AcceptsEquivalentContinuityPhrasing()
    {
        var engine = new HardcoreWritingEngine(CreateSnapshotService());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "承接上一章继续调查蓝光异常" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            WorldRules = { "雾潮城临近逆潮夜，雾灯必须点亮。" },
            CharacterStates = { "林澈：雾灯修补工，正在前往第七环区查看蓝光异常" },
            ChapterBlueprints = { "先承接巡廊移动，再进入档案调查。" },
            PreviousSummaries = { "chapter-001: 林澈离开安全区域，沿巡廊向骨塔中层移动，浓雾吞没原位，孤灯独立。" },
            HardContinuityFacts =
            {
                "主角姓名：林澈",
                "主角当前状态：违反规定，正在沿巡廊前往骨塔中层查看蓝光异常",
                "上一章结尾状态：林澈离开安全区域，沿巡廊向骨塔中层移动，浓雾吞没原位，孤灯独立",
                "下一章必须承接：林澈正在前往第七环区查看蓝光异常",
                "下一章必须承接：听潮骨感知到的异常震动未解释",
                "下一章必须承接：蓝光现象未解释"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第二章：骨牵蓝引
            林澈离开第三环区那盏刚被点亮的雾灯不过二十来步，身后的光晕便被浓雾彻底吞没。他沿着潮汐骨塔外侧巡廊继续向上，违规进入中层档案区的边缘，只为确认第七环区那抹蓝光异常。
            听潮骨仍在发热，异常震动像一根细线牵着他往前走，但它还无法解释蓝磷骨光的来源，这个现象仍像冷火一样悬在前方。
            <chapter_changes>{"CharacterStateChanges":["林澈从第三环区巡廊进入中层档案区边缘"],"ConflictProgress":["蓝光异常从目击变为调查目标"],"NewPlotPoints":["林澈准备调查第七平台旧档案"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["林澈沿巡廊向第七环区移动"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["林澈从第三环区巡廊进入中层档案区边缘"],"ConflictProgress":["蓝光异常从目击变为调查目标"],"NewPlotPoints":["林澈准备调查第七平台旧档案"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["林澈沿巡廊向第七环区移动"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.NotEqual("gate_failed", report.Status);
        Assert.DoesNotContain(report.Issues, issue => issue.Contains("核心连续性失败"));
    }

    [Fact]
    public async Task ValidateDraftAsync_AcceptsSemanticCarryForEscalatingThreat()
    {
        var engine = new HardcoreWritingEngine(CreateSnapshotService());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-003",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "逆潮夜提前爆发，第七平台钟楼浮出水面" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-003",
            WorldRules = { "逆潮夜海水倒流，雾灯必须点亮。" },
            CharacterStates = { "林澈：雾灯修补工，正在调查第七平台钟楼蓝光。" },
            PreviousSummaries = { "chapter-002: 林澈和岑鸢获得镇潮骨纪要，逆潮夜临近，威胁增加。" },
            HardContinuityFacts =
            {
                "主角姓名：林澈",
                "下一章必须承接：逆潮夜临近，威胁增加"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第三章：逆潮夜来临
            林澈刚被带进监察科，潮汐骨塔深处就传来闷响。传声筒里确认逆潮夜提前爆发，海水已经开始倒流，所有人员进入最高戒备。
            他借助听潮骨看见第七平台方向出现巨大漩涡，沉没钟楼正在浮出水面。岑鸢作为外勤记录员跟在他身侧，记录蓝光与潮声共鸣的异常。
            <chapter_changes>{"CharacterStateChanges":["林澈在逆潮夜提前爆发后前往第七平台"],"ConflictProgress":["逆潮夜从临近威胁升级为正在爆发的生存危机"],"NewPlotPoints":["沉没钟楼浮出水面"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["林澈和岑鸢前往第七平台"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["林澈在逆潮夜提前爆发后前往第七平台"],"ConflictProgress":["逆潮夜从临近威胁升级为正在爆发的生存危机"],"NewPlotPoints":["沉没钟楼浮出水面"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["林澈和岑鸢前往第七平台"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.True(report.Status != "gate_failed", string.Join("；", report.Issues));
        Assert.DoesNotContain(report.Issues, issue => issue.Contains("未承接"));
    }

    [Fact]
    public async Task ValidateDraftAsync_AcceptsEventEscalationAsThreatCarry()
    {
        var engine = new HardcoreWritingEngine(CreateSnapshotService());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-003",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "逆潮夜提前爆发，第七平台钟楼浮出水面" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-003",
            WorldRules = { "逆潮夜海水倒流，雾灯必须点亮。" },
            CharacterStates = { "林澈：雾灯修补工，正在调查第七平台钟楼蓝光。" },
            PreviousSummaries = { "chapter-002: 林澈和岑鸢获得镇潮骨纪要，逆潮夜临近，威胁增加。" },
            HardContinuityFacts =
            {
                "主角姓名：林澈",
                "下一章必须承接：逆潮夜临近，威胁增加"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第三章：逆潮夜来临
            林澈刚被带进监察科，潮汐骨塔深处就传来闷响。传声筒里确认逆潮夜提前爆发，海水已经开始倒流。
            他借助听潮骨看见第七平台方向出现巨大漩涡，沉没钟楼正在浮出水面。岑鸢作为外勤记录员跟在他身侧，记录蓝光与潮声共鸣的异常。
            <chapter_changes>{"CharacterStateChanges":["林澈在逆潮夜提前爆发后前往第七平台"],"ConflictProgress":["逆潮夜从临近转为正在爆发"],"NewPlotPoints":["沉没钟楼浮出水面"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["林澈和岑鸢前往第七平台"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["林澈在逆潮夜提前爆发后前往第七平台"],"ConflictProgress":["逆潮夜从临近转为正在爆发"],"NewPlotPoints":["沉没钟楼浮出水面"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["林澈和岑鸢前往第七平台"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.True(report.Status != "gate_failed", string.Join("；", report.Issues));
        Assert.DoesNotContain(report.Issues, issue => issue.Contains("未承接"));
    }
}

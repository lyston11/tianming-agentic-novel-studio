using Moq;
using System.Reflection;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Tracking.Rules;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;
using Xunit;

namespace Tests.Unit.Services.NovelAgent;

public class HardcoreWritingEngineTests
{
    [Fact]
    public void StripChanges_RemovesXmlChapterChangesBlockFromCommittedContent()
    {
        var engine = CreateEngineWithRealGate();

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
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()));

    private static IChapterPromptBuilder CreatePromptBuilder() =>
        new ChapterPromptBuilder(new ChapterDirectiveBuilder());

    private const string MinimalChangesJson = """
    {
      "CharacterStateChanges": [],
      "ConflictProgress": [],
      "NewPlotPoints": [],
      "ForeshadowingActions": [],
      "LocationStateChanges": [],
      "FactionStateChanges": [],
      "TimeProgression": {},
      "CharacterMovements": [],
      "ItemTransfers": [],
      "SecretRevealChanges": [],
      "PledgeConstraintChanges": [],
      "DeadlineConstraintChanges": []
    }
    """;

    private static HardcoreWritingEngine CreateEngineWithRealGate(
        IGeneratedContentService? generatedContent = null,
        IChapterFactPostCommitScheduler? chapterFactPostCommitScheduler = null)
    {
        var guideContext = new Mock<IGuideContextService>();
        guideContext
            .Setup(x => x.BuildContentContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string chapterId, CancellationToken _) => new ContentTaskContext
            {
                ChapterId = chapterId,
                FactSnapshot = new FactSnapshot(),
                ContextIds = new ContextIdCollection()
            });

        var gate = new GenerationGate(
            new LedgerConsistencyChecker(),
            new LedgerRuleSetProvider(),
            new EntityOmissionDetector(null!));

        return new HardcoreWritingEngine(
            CreateSnapshotService(),
            guideContext.Object,
            gate,
            generatedContent ?? Mock.Of<IGeneratedContentService>(),
            Mock.Of<IContentChunkSearchService>(),
            settingsManager: new ConfiguredWritingSettingsManager(),
            chapterFactPostCommitScheduler: chapterFactPostCommitScheduler);
    }

    private static HardcoreWritingEngine CreateEngineWithoutGenerationGate(
        IChapterGatekeeper? chapterGatekeeper = null,
        IGeneratedContentService? generatedContent = null)
    {
        var guideContext = new Mock<IGuideContextService>();
        guideContext
            .Setup(x => x.BuildContentContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string chapterId, CancellationToken _) => new ContentTaskContext
            {
                ChapterId = chapterId,
                ContextMode = ContentContextMode.Full,
                FactSnapshot = new FactSnapshot(),
                ContextIds = new ContextIdCollection()
            });
        var snapshotService = new StoryStateSnapshotService(
            guideContext.Object,
            Mock.Of<IContentChunkSearchService>(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()));

        return new HardcoreWritingEngine(
            snapshotService,
            guideContext.Object,
            generationGate: null!,
            generatedContent ?? Mock.Of<IGeneratedContentService>(),
            contentChunkSearch: Mock.Of<IContentChunkSearchService>(),
            settingsManager: new ConfiguredWritingSettingsManager(),
            chapterGatekeeper: chapterGatekeeper);
    }

    [Fact]
    public async Task BuildContextPackageAsync_BackfillsCommittedPreviousChapterAfterRealGuidePackage()
    {
        var guideContext = new Mock<IGuideContextService>();
        guideContext
            .Setup(x => x.BuildContentContextAsync("chapter-002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentTaskContext
            {
                ChapterId = "chapter-002",
                ContextMode = ContentContextMode.Full,
                FactSnapshot = new FactSnapshot(),
                ContextIds = new ContextIdCollection()
            });

        var generatedContent = new Mock<IGeneratedContentService>();
        generatedContent
            .Setup(x => x.GetChapterAsync("chapter-001"))
            .ReturnsAsync("上一章主角在沉船坞遭遇黑潮异兽，修好旧式潜航机甲并首次反杀。");

        var snapshotService = new StoryStateSnapshotService(
            guideContext.Object,
            Mock.Of<IContentChunkSearchService>(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()));

        var engine = new HardcoreWritingEngine(
            snapshotService,
            guideContext.Object,
            generationGate: null!,
            generatedContent.Object,
            contentChunkSearch: Mock.Of<IContentChunkSearchService>(),
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
        Assert.Equal("context_ready:real_project_data", package.Status);
    }

    [Fact]
    public async Task BuildContextPackageAsync_FailsWhenGuidePackageIsMissing()
    {
        var guideContext = new Mock<IGuideContextService>();
        guideContext
            .Setup(x => x.BuildContentContextAsync("chapter-002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentTaskContext?)null);

        var snapshotService = new StoryStateSnapshotService(
            guideContext.Object,
            Mock.Of<IContentChunkSearchService>(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()));

        var engine = new HardcoreWritingEngine(
            snapshotService,
            guideContext.Object,
            generationGate: null!,
            Mock.Of<IGeneratedContentService>(),
            contentChunkSearch: Mock.Of<IContentChunkSearchService>(),
            settingsManager: new object());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.BuildContextPackageAsync(
            new NovelAgentRun
            {
                TargetChapterId = "chapter-002",
                ChapterBrief = new ChapterCreativeBrief
                {
                    ChapterId = "chapter-002",
                    CoreIdea = "主角继续打怪升级"
                }
            },
            new StoryBibleDocument()));

        Assert.Contains("BuildContentContextAsync", ex.Message, StringComparison.Ordinal);
        Assert.Contains("真实章节上下文", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildContextPackageAsync_IncludesPreviousContinuityFactsAsHardFacts()
    {
        var engine = CreateEngineWithRealGate();
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
        var engine = CreateEngineWithoutGenerationGate();
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
        Assert.Contains(report.Issues, issue => issue.Contains("GenerationGate") && issue.Contains("未配置"));
        Assert.Contains(report.Issues, issue => issue.Contains("主角") && issue.Contains("陈默"));
        Assert.False(report.ProtocolPassed);
        Assert.False(report.FactSnapshotPassed);
        Assert.False(report.BlueprintPassed);
        Assert.False(report.RagPassed);
    }

    [Fact]
    public async Task ValidateDraftAsync_ReportsGateUnavailableWithoutFallbackValidation()
    {
        var gatekeeper = new RecordingChapterGatekeeper();
        var engine = CreateEngineWithoutGenerationGate(chapterGatekeeper: gatekeeper);
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-001",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "开篇建立主角和旧邮路" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-001",
            WorldRules = { "旧邮路只在雨后显形。" },
            ChapterBlueprints = { "沈砚收到第一封无法投递的信。" }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            沈砚收到第一封无法投递的信。
            <chapter_changes>{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}"""
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.True(gatekeeper.WasCalled);
        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("GenerationGate") && issue.Contains("未配置"));
        Assert.Contains("测试 Gatekeeper 已接管硬门禁。", report.Issues);
        Assert.False(report.ProtocolPassed);
        Assert.False(report.FactSnapshotPassed);
        Assert.False(report.BlueprintPassed);
        Assert.False(report.RagPassed);
    }

    [Fact]
    public async Task CommitChapterAsync_SavesCommittedContentWithoutInlineBackgroundRefresh()
    {
        var generatedContent = new Mock<IGeneratedContentService>();
        var engine = CreateEngineWithRealGate(generatedContent.Object);
        var run = new NovelAgentRun
        {
            RunId = "run-commit-1",
            TargetChapterId = "chapter-001",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "开篇建立主角和旧邮路" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-001",
            WorldRules = { "旧邮路只在雨后显形。" },
            ChapterBlueprints = { "沈砚收到第一封无法投递的信。" }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            沈砚收到第一封无法投递的信。
            <chapter_changes>{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var impact = await engine.CommitChapterAsync(run, context, draft);

        Assert.Equal("clean", impact.Status);
        Assert.Contains("chapter-001", impact.ImpactedChapters);
        generatedContent.Verify(x => x.SaveChapterAsync("chapter-001", It.Is<string>(content =>
            content.Contains("沈砚收到第一封无法投递的信") &&
            !content.Contains("chapter_changes", StringComparison.OrdinalIgnoreCase))), Times.Once);
    }

    [Fact]
    public async Task CommitChapterAsync_PassesSelectedCandidateTitleToMetadataAwareStore()
    {
        var generatedContent = new RecordingGeneratedContentService();
        var engine = CreateEngineWithRealGate(generatedContent);
        var run = new NovelAgentRun
        {
            RunId = "run-commit-title",
            TargetChapterId = "chapter-006",
            ChapterBrief = new ChapterCreativeBrief
            {
                SelectedCandidateTitle = "主管道深处的残响",
                RecommendedCandidateTitle = "备用标题"
            }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-006",
            WorldRules = { "银蓝邮徽只能辨认邮路残留。" },
            ChapterBlueprints = { "沈砚进入主管道深处。" }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            沈砚进入主管道深处，银蓝邮徽贴着胸口发烫。
            <chapter_changes>{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        await engine.CommitChapterAsync(run, context, draft);

        Assert.Equal("chapter-006", generatedContent.ChapterId);
        Assert.Equal("主管道深处的残响", generatedContent.Title);
        Assert.Contains("沈砚进入主管道深处", generatedContent.Content);
        Assert.DoesNotContain("chapter_changes", generatedContent.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommitChapterAsync_AttachesContinuityFactsWhenSchedulerCompletes()
    {
        var generatedContent = new Mock<IGeneratedContentService>();
        var factWriter = new RecordingChapterFactWriter(new ChapterContinuityFacts
        {
            ChapterId = "chapter-001",
            ChapterTitle = "第一章 银蓝邮徽",
            ProtagonistName = "沈砚",
            ProtagonistIdentity = "低阶星渊邮差",
            ProtagonistStatus = "负伤但清醒",
            CurrentLocation = "废弃邮局地下室",
            KeyEvents = { "沈砚觉醒银蓝邮徽" },
            EndingState = "银蓝邮徽指向旧邮路入口",
            NextChapterMustCarry = { "承接旧邮路入口" }
        });
        var engine = CreateEngineWithRealGate(
            generatedContent.Object,
            new InlineChapterFactPostCommitScheduler(factWriter));
        var run = new NovelAgentRun
        {
            RunId = "run-commit-1",
            TargetChapterId = "chapter-001",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "开篇建立主角和旧邮路" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-001",
            ChapterBlueprints = { "沈砚收到第一封无法投递的信。" }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            沈砚收到第一封无法投递的信。
            <chapter_changes>{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        await engine.CommitChapterAsync(run, context, draft);

        Assert.True(factWriter.WasCalled);
        Assert.NotNull(run.ContinuityFacts);
        Assert.Equal("沈砚", run.ContinuityFacts!.ProtagonistName);
        Assert.Equal("run-commit-1", run.ContinuityFacts.SourceRunId);
        Assert.Contains(run.Notes, note => note.Contains("章节连续性事实已由 LLM 沉淀", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommitChapterAsync_SchedulesContinuityFactsAfterCommitWhenSchedulerExists()
    {
        var generatedContent = new Mock<IGeneratedContentService>();
        var scheduler = new RecordingChapterFactPostCommitScheduler();
        var engine = CreateEngineWithRealGate(
            generatedContent.Object,
            scheduler);
        var run = new NovelAgentRun
        {
            RunId = "run-commit-bg",
            TargetChapterId = "chapter-001",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "提交后后台沉淀事实" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-001",
            ChapterBlueprints = { "沈砚收到第一封无法投递的信。" }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            沈砚收到第一封无法投递的信。
            <chapter_changes>{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var impact = await engine.CommitChapterAsync(run, context, draft);

        Assert.Equal("clean", impact.Status);
        Assert.True(scheduler.WasScheduled);
        Assert.NotNull(scheduler.Request);
        Assert.DoesNotContain("chapter_changes", scheduler.Request!.CommittedContent, StringComparison.OrdinalIgnoreCase);
        Assert.Null(run.ContinuityFacts);
        Assert.Contains(run.Notes, note => note.Contains("后台沉淀"));
        generatedContent.Verify(x => x.SaveChapterAsync("chapter-001", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RepairDraftAsync_UsesInjectedChapterRewriter()
    {
        var rewriter = new RecordingChapterRewriter();
        var engine = new HardcoreWritingEngine(
            CreateSnapshotService(),
            Mock.Of<IGuideContextService>(),
            generationGate: null!,
            Mock.Of<IGeneratedContentService>(),
            Mock.Of<IContentChunkSearchService>(),
            settingsManager: new ConfiguredWritingSettingsManager(),
            chapterRewriter: rewriter);
        var run = new NovelAgentRun { TargetChapterId = "chapter-003" };
        var context = new ChapterContextPackageSummary { ChapterId = "chapter-003" };
        var draft = new ChapterDraftArtifact
        {
            ArtifactId = "draft-repair-1",
            DraftContent = "旧草稿\n<chapter_changes>{}</chapter_changes>",
            RepairAttemptCount = 1
        };
        var report = new GenerationGateReport
        {
            Issues = { "CHANGES JSON 不是可解析对象。" }
        };

        var repaired = await engine.RepairDraftAsync(run, context, draft, report);

        Assert.True(rewriter.WasCalled);
        Assert.Equal("chapter-003", rewriter.Request?.Run.TargetChapterId);
        Assert.Equal("draft-repair-1", rewriter.Request?.Draft.ArtifactId);
        Assert.Equal("rewriter_result", repaired.Status);
        Assert.Equal(2, repaired.RepairAttemptCount);
    }

    [Fact]
    public void ChapterDirectiveBuilder_IncludesAcceptedCreativeIntentsAsProductionRequirements()
    {
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            UserGoal = "继续第二章生产"
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            ChapterBlueprints = { "陈默离开维修站，进入旧邮路。" },
            AcceptedCreativeIntents =
            {
                new AcceptedCreativeIntentSnapshot
                {
                    IntentId = "intent-chapter-002",
                    NormalizedIntent = "第二章主冲突改为怪物围攻，男主用银蓝邮徽识别逃生路线。",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-002",
                    ImpactLevel = "chapter_rewrite",
                    Source = "chat"
                },
                new AcceptedCreativeIntentSnapshot
                {
                    IntentId = "intent-project-mainline",
                    NormalizedIntent = "旧邮路成为第一卷主线，每次开启都要付出记忆代价。",
                    TargetScope = "project",
                    ImpactLevel = "mainline_change",
                    Source = "agent_suggestion"
                }
            }
        };

        var directive = new ChapterDirectiveBuilder().Build(run, context);

        Assert.Contains(directive.CreativeRequirements,
            item => item.Contains("本章必须执行", StringComparison.Ordinal) &&
                    item.Contains("怪物围攻", StringComparison.Ordinal));
        Assert.Contains(directive.CreativeRequirements,
            item => item.Contains("后续方向参考", StringComparison.Ordinal) &&
                    item.Contains("记忆代价", StringComparison.Ordinal));
        Assert.Contains(directive.AcceptanceRules,
            item => item.Contains("creativeRequirements", StringComparison.Ordinal) &&
                    item.Contains("已采纳创意", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateDraftAsync_FailsWhenChapterAcceptedCreativeIntentIsNotExecuted()
    {
        var engine = CreateEngineWithRealGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "承接上一章推进旧邮路" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            WorldRules = { "银蓝邮徽只能辨认被篡改邮路，不能攻击。" },
            CharacterStates = { "陈默：维修站青年，持有银蓝邮徽" },
            ChapterBlueprints = { "陈默离开维修站，进入旧邮路。" },
            PreviousSummaries = { "chapter-001: 陈默获得银蓝邮徽，维修站外黑雨逼近。" },
            AcceptedCreativeIntents =
            {
                new AcceptedCreativeIntentSnapshot
                {
                    IntentId = "intent-chapter-002",
                    NormalizedIntent = "第二章主冲突改为怪物围攻，男主用银蓝邮徽识别逃生路线。",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-002",
                    ImpactLevel = "chapter_rewrite",
                    Source = "chat"
                }
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第二章：旧站谈判
            陈默离开维修站后，在旧站大厅和女机械师长谈。他们讨论同盟条件、资源分配和彼此信任，没有遭遇怪物，也没有寻找逃生路线。
            <chapter_changes>{"CharacterStateChanges":["陈默与女机械师建立初步信任"],"ConflictProgress":["同盟谈判展开"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["陈默进入旧站大厅"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["陈默与女机械师建立初步信任"],"ConflictProgress":["同盟谈判展开"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["陈默进入旧站大厅"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.False(report.BlueprintPassed);
        Assert.Contains(report.Issues, issue => issue.Contains("已采纳创意未执行") && issue.Contains("怪物围攻"));
        Assert.Contains(report.RepairHints, hint => hint.Contains("按已采纳创意重写"));
    }

    [Fact]
    public async Task ValidateDraftAsync_DoesNotForceProjectWideCreativeIntentIntoEveryChapter()
    {
        var engine = CreateEngineWithRealGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "承接上一章进入旧站" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            WorldRules = { "银蓝邮徽只能辨认被篡改邮路，不能攻击。" },
            CharacterStates = { "陈默：维修站青年，持有银蓝邮徽" },
            ChapterBlueprints = { "陈默进入旧站，调查第一条邮路线索。" },
            PreviousSummaries = { "chapter-001: 陈默获得银蓝邮徽，维修站外黑雨逼近。" },
            AcceptedCreativeIntents =
            {
                new AcceptedCreativeIntentSnapshot
                {
                    IntentId = "intent-project-mainline",
                    NormalizedIntent = "旧邮路成为第一卷主线，每次开启都要付出记忆代价。",
                    TargetScope = "project",
                    ImpactLevel = "mainline_change",
                    Source = "agent_suggestion"
                }
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = $"""
            第二章：旧站暗格
            陈默沿着维修站外墙进入旧站，银蓝邮徽只能辨认被篡改邮路，它在腕骨旁发冷，提示他避开断裂的投递线。他没有获得新能力，只确认第一条线索通向地下分拣口。
            <chapter_changes>
            {MinimalChangesJson}
            </chapter_changes>
            """,
            ChangesJson = MinimalChangesJson,
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.NotEqual("gate_failed", report.Status);
        Assert.DoesNotContain(report.Issues, issue => issue.Contains("已采纳创意未执行"));
    }

    [Fact]
    public async Task ValidateDraftAsync_AcceptsEquivalentContinuityPhrasing()
    {
        var engine = CreateEngineWithRealGate();
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
            DraftContent = $"""
            第二章：骨牵蓝引
            林澈离开第三环区那盏刚被点亮的雾灯不过二十来步，身后的光晕便被浓雾彻底吞没。他沿着潮汐骨塔外侧巡廊继续向上，违规进入中层档案区的边缘，只为确认第七环区那抹蓝光异常。
            听潮骨仍在发热，异常震动像一根细线牵着他往前走，但它还无法解释蓝磷骨光的来源，这个现象仍像冷火一样悬在前方。
            <chapter_changes>
            {MinimalChangesJson}
            </chapter_changes>
            """,
            ChangesJson = MinimalChangesJson,
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.NotEqual("gate_failed", report.Status);
        Assert.DoesNotContain(report.Issues, issue => issue.Contains("核心连续性失败"));
    }

    [Fact]
    public async Task ValidateDraftAsync_AcceptsSemanticCarryForEscalatingThreat()
    {
        var engine = CreateEngineWithRealGate();
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
            DraftContent = $"""
            第三章：逆潮夜来临
            林澈刚被带进监察科，潮汐骨塔深处就传来闷响。传声筒里确认逆潮夜提前爆发，海水已经开始倒流，所有人员进入最高戒备。
            他借助听潮骨看见第七平台方向出现巨大漩涡，沉没钟楼正在浮出水面。岑鸢作为外勤记录员跟在他身侧，记录蓝光与潮声共鸣的异常。
            <chapter_changes>
            {MinimalChangesJson}
            </chapter_changes>
            """,
            ChangesJson = MinimalChangesJson,
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.True(report.Status != "gate_failed", string.Join("；", report.Issues));
        Assert.DoesNotContain(report.Issues, issue => issue.Contains("未承接"));
    }

    [Fact]
    public async Task ValidateDraftAsync_AcceptsEventEscalationAsThreatCarry()
    {
        var engine = CreateEngineWithRealGate();
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
            DraftContent = $"""
            第三章：逆潮夜来临
            林澈刚被带进监察科，潮汐骨塔深处就传来闷响。传声筒里确认逆潮夜提前爆发，海水已经开始倒流。
            他借助听潮骨看见第七平台方向出现巨大漩涡，沉没钟楼正在浮出水面。岑鸢作为外勤记录员跟在他身侧，记录蓝光与潮声共鸣的异常。
            <chapter_changes>
            {MinimalChangesJson}
            </chapter_changes>
            """,
            ChangesJson = MinimalChangesJson,
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.True(report.Status != "gate_failed", string.Join("；", report.Issues));
        Assert.DoesNotContain(report.Issues, issue => issue.Contains("未承接"));
    }

    [Fact]
    public async Task ValidateDraftAsync_AcceptsCharacterFateContinuityWhenAbstractCarryIsConcretized()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-005",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "承接上一章女子身份与命运继续推进" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-005",
            CharacterStates = { "沈砚：携带银蓝邮徽继续投递任务" },
            PreviousSummaries = { "chapter-004: 薇拉为掩护沈砚被工会小队带走。" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：女子的身份和命运"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第五章：雾障追邮
            沈砚没有把薇拉最后的身影从脑海里赶出去。她并没有立刻被处决，而是以信标守望者的身份暴露为代价，被工会鬣狗小队押解离开。
            银蓝邮徽只能辨认和溯源邮路，无法替他救人，但徽面残留的微弱连接告诉他：薇拉暂时存活，命运未知，押送方向指向工会据点。
            <chapter_changes>{"CharacterStateChanges":["薇拉身份暴露，被工会押解离开，暂时存活但命运未知"],"ConflictProgress":["沈砚确认薇拉被押往工会据点"],"NewPlotPoints":["银蓝邮徽溯源出押送方向"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚追踪工会押送路线"],"ItemTransfers":[],"SecretRevealChanges":["薇拉是信标守望者"],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["薇拉身份暴露，被工会押解离开，暂时存活但命运未知"],"ConflictProgress":["沈砚确认薇拉被押往工会据点"],"NewPlotPoints":["银蓝邮徽溯源出押送方向"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚追踪工会押送路线"],"ItemTransfers":[],"SecretRevealChanges":["薇拉是信标守望者"],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.DoesNotContain(report.Issues, issue => issue.Contains("女子的身份和命运"));
        Assert.DoesNotContain(report.Issues, issue => issue.Contains("核心连续性失败"));
    }

    [Fact]
    public async Task ValidateDraftAsync_FailsWhenSilverBadgeViolatesKnowledgeBoundary()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-006",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "主角寻找升级资源但必须遵守银蓝邮徽规则" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-006",
            WorldRules =
            {
                "知识库硬事实：银蓝邮徽只能辨认/溯源邮路，不能攻击或治愈，不能解锁其他战斗或修复能力。"
            },
            CharacterStates = { "沈砚：携带银蓝邮徽继续投递任务" },
            PreviousSummaries = { "chapter-005: 沈砚确认银蓝邮徽只能辨认和溯源邮路。" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：银蓝邮徽只能辨认/溯源邮路，不能攻击或治愈"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第六章：潮信核心
            沈砚暴露了银蓝邮徽能激活锈蚀功能，随后邮徽升级，新增了干扰追踪的能力。
            <chapter_changes>{"CharacterStateChanges":["沈砚让银蓝邮徽获得新增能力"],"ConflictProgress":["沈砚用邮徽激活锈蚀功能"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["沈砚让银蓝邮徽获得新增能力"],"ConflictProgress":["沈砚用邮徽激活锈蚀功能"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("银蓝邮徽") && issue.Contains("能力边界"));
        var check = Assert.Single(report.KnowledgeConstraintChecks);
        Assert.Equal("failed", check.Status);
        Assert.Equal("银蓝邮徽能力边界", check.Title);
        Assert.Equal("HardFact", check.EntryType);
        Assert.Contains(check.Violations, violation => violation.Contains("攻击功能", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateDraftAsync_UsesBoundKnowledgeHardFactsForBoundaryGate()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-006",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "主角使用银蓝邮徽追踪旧邮路" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-006",
            KnowledgeBindings =
            {
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "hardfact-1",
                    EntryType = "HardFact",
                    Title = "银蓝邮徽能力边界",
                    Content = "银蓝邮徽只能辨认被篡改邮路，不能攻击、治愈、激活或升级。",
                    ProjectUsageStatus = "referenced"
                }
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第六章：蓝徽暴走
            沈砚抬起右手，银蓝邮徽突然激活了攻击功能，蓝光撞碎追兵的骨甲。
            <chapter_changes>{"CharacterStateChanges":[],"ConflictProgress":["沈砚用银蓝邮徽攻击追兵"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":[],"ConflictProgress":["沈砚用银蓝邮徽攻击追兵"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("银蓝邮徽") && issue.Contains("能力边界"));
    }

    [Fact]
    public async Task ValidateDraftAsync_FailsWhenLimitedUseHardFactIsExpandedIntoNewFunction()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-006",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "主角寻找升级资源但必须遵守银蓝邮徽规则" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-006",
            WorldRules =
            {
                "知识库硬事实：【沈砚】银蓝邮徽（右手腕）：仅在潮汐骨光出现时短暂发亮，用于辨认被篡改的邮路。不能攻击，不能治愈。这些是主角能力与核心道具的根本边界，不可逾越或凭空强化。"
            },
            CharacterStates = { "沈砚：携带银蓝邮徽继续投递任务" },
            PreviousSummaries = { "chapter-005: 沈砚确认银蓝邮徽仅用于辨认被篡改的邮路。" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：银蓝邮徽仅用于辨认被篡改的邮路，不能攻击、治愈或凭空强化"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第六章：潮信核心
            沈砚查看邮差终端，追兵被舱门暂时阻挡，尤其在他暴露了银蓝邮徽能激活锈蚀功能之后。
            <chapter_changes>{"CharacterStateChanges":["沈砚暴露银蓝邮徽的新功能"],"ConflictProgress":["沈砚用邮徽激活锈蚀功能"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["沈砚暴露银蓝邮徽的新功能"],"ConflictProgress":["沈砚用邮徽激活锈蚀功能"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("银蓝邮徽") && issue.Contains("能力边界"));
    }

    [Fact]
    public async Task ValidateDraftAsync_FailsWhenLimitedUseToolGetsPackagedUpgrade()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-006",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "主角只能用银蓝邮徽辨认残留信标" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-006",
            WorldRules =
            {
                "知识库硬事实：银蓝邮徽只能辨认/溯源邮路和残留信标，不能攻击、治愈、激活、腐蚀、锈蚀、干扰或新增战斗能力。"
            },
            CharacterStates = { "沈砚：携带银蓝邮徽继续投递任务" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：银蓝邮徽只能辨认/溯源邮路和残留信标，不能新增能力或强化"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第六章：潮信核心
            沈砚发现银蓝邮徽基础共鸣辨识单元获得强化，能把残留信标扩展成更强的追踪场。
            <chapter_changes>{"CharacterStateChanges":["银蓝邮徽基础共鸣辨识单元获得强化"],"ConflictProgress":["沈砚依靠强化后的邮徽摆脱追踪"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["银蓝邮徽基础共鸣辨识单元获得强化"],"ConflictProgress":["沈砚依靠强化后的邮徽摆脱追踪"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("银蓝邮徽") && issue.Contains("能力边界"));
    }

    [Fact]
    public async Task ValidateDraftAsync_FailsWhenAnyLimitedUseToolGainsUnlistedFunction()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-012",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "主角进入旧库但必须遵守黑曜钥匙规则" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-012",
            WorldRules =
            {
                "知识库硬事实：【顾衡】黑曜钥匙：仅在旧锁共鸣时短暂发热，用于开启旧锁和辨认旧库门牌。不能攻击，不能治愈。这是黑曜钥匙的根本边界，不可逾越或凭空强化。"
            },
            CharacterStates = { "沈砚：携带黑曜钥匙进入旧库" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：黑曜钥匙仅用于开启旧锁和辨认旧库门牌，不能攻击、治愈或凭空强化"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第十二章：旧库门牌
            沈砚把黑曜钥匙贴在门缝上，黑曜钥匙能激活腐蚀门禁功能，锁舌随即融开。
            <chapter_changes>{"CharacterStateChanges":["黑曜钥匙获得腐蚀门禁功能"],"ConflictProgress":["沈砚用黑曜钥匙腐蚀门禁"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["黑曜钥匙获得腐蚀门禁功能"],"ConflictProgress":["沈砚用黑曜钥匙腐蚀门禁"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("黑曜钥匙") && issue.Contains("能力边界"));
    }

    [Fact]
    public async Task ValidateDraftAsync_AllowsExplicitlyNegatedSilverBadgeActivationBoundary()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-007",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "主角沿邮路追踪残留信标" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-007",
            WorldRules =
            {
                "知识库硬事实：银蓝邮徽只能辨认/溯源邮路，不能攻击或治愈，不能激活机关或新增战斗能力。"
            },
            CharacterStates = { "沈砚：携带银蓝邮徽继续投递任务" },
            PreviousSummaries = { "chapter-006: 沈砚确认银蓝邮徽只能辨认和溯源邮路。" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：银蓝邮徽只能辨认/溯源邮路，不能攻击、治愈或激活机关"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第七章：残留信标
            沈砚抬起左手，银蓝邮徽只是辨认出邮路残留的信标方向，它本身并未激活机关，也没有释放任何功能。
            他循着残留信标绕开工会追踪者，确认第九枚空邮票仍指向六环航道深处。
            <chapter_changes>{"CharacterStateChanges":["沈砚确认银蓝邮徽仍只能辨认邮路残留信标"],"ConflictProgress":["沈砚绕开工会追踪者"],"NewPlotPoints":["第九枚空邮票指向六环航道深处"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚沿残留信标继续追踪"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["沈砚确认银蓝邮徽仍只能辨认邮路残留信标"],"ConflictProgress":["沈砚绕开工会追踪者"],"NewPlotPoints":["第九枚空邮票指向六环航道深处"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚沿残留信标继续追踪"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.DoesNotContain(report.Issues, issue => issue.Contains("银蓝邮徽") && issue.Contains("能力边界"));
    }

    [Fact]
    public async Task ValidateDraftAsync_AllowsOtherObjectRustNearLimitedUseTool()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-006",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "主角沿残留信标进入维修通道" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-006",
            WorldRules =
            {
                "知识库硬事实：银蓝邮徽只能辨认/溯源邮路和残留信标，不能攻击、治愈、激活、腐蚀、锈蚀、干扰或新增战斗能力。"
            },
            CharacterStates = { "沈砚：携带银蓝邮徽继续投递任务" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：银蓝邮徽只能辨认/溯源邮路和残留信标"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第六章：潮信核心
            银蓝邮徽在之前与怀表内锈蚀核心相邻时，只辨认出残留信标的方向；真正发出锈蚀气息的是怀表内的旧核心。
            <chapter_changes>{"CharacterStateChanges":["沈砚确认银蓝邮徽只用于辨认残留信标"],"ConflictProgress":["沈砚继续沿维修通道前进"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["沈砚确认银蓝邮徽只用于辨认残留信标"],"ConflictProgress":["沈砚继续沿维修通道前进"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.DoesNotContain(report.Issues, issue => issue.Contains("银蓝邮徽") && issue.Contains("能力边界"));
    }

    [Fact]
    public async Task ValidateDraftAsync_DoesNotJoinHealingAndSilverBadgeAcrossSentences()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-007",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "主角负伤后继续追踪邮路" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-007",
            WorldRules =
            {
                "知识库硬事实：银蓝邮徽只能辨认/溯源邮路，不能攻击或治愈，不能激活机关或新增战斗能力。"
            },
            CharacterStates = { "沈砚：携带银蓝邮徽继续投递任务" },
            PreviousSummaries = { "chapter-006: 沈砚确认银蓝邮徽只能辨认和溯源邮路。" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：银蓝邮徽只能辨认/溯源邮路"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第七章：残留信标
            沈砚需要找到老针，治愈自己的伤势。他停在岔口前，银蓝邮徽只辨认出邮路残留信标，提示方向指向东七区。
            <chapter_changes>{"CharacterStateChanges":["沈砚负伤并寻找老针治疗"],"ConflictProgress":["沈砚继续追踪东七区邮路"],"NewPlotPoints":["银蓝邮徽辨认出邮路残留信标"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚前往东七区"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["沈砚负伤并寻找老针治疗"],"ConflictProgress":["沈砚继续追踪东七区邮路"],"NewPlotPoints":["银蓝邮徽辨认出邮路残留信标"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚前往东七区"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.DoesNotContain(report.Issues, issue => issue.Contains("银蓝邮徽") && issue.Contains("能力边界"));
    }

    [Fact]
    public async Task ValidateDraftAsync_FailsWhenAnyKnowledgeHardFactSubjectViolatesBoundary()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-012",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "主角用黑曜钥匙进入旧库" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-012",
            WorldRules =
            {
                "知识库硬事实：黑曜钥匙只能开启旧锁和辨认旧库门牌，不能攻击、治愈、修复、腐蚀门禁或新增战斗能力。"
            },
            CharacterStates = { "沈砚：携带黑曜钥匙进入旧库" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：黑曜钥匙只能开启旧锁，不能攻击或修复"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第十二章：旧库门牌
            沈砚抬起黑曜钥匙，钥匙释放出一圈冲击波，攻击了守门追兵，并顺手修复了门禁裂缝。
            <chapter_changes>{"CharacterStateChanges":["沈砚使用黑曜钥匙攻击追兵"],"ConflictProgress":["沈砚突破旧库门禁"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚进入旧库"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["沈砚使用黑曜钥匙攻击追兵"],"ConflictProgress":["沈砚突破旧库门禁"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚进入旧库"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.Equal("gate_failed", report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("黑曜钥匙") && issue.Contains("能力边界"));
    }

    [Fact]
    public async Task ValidateDraftAsync_DoesNotJoinGenericForbiddenActionAcrossSentences()
    {
        var engine = CreateEngineWithoutGenerationGate();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-012",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "主角负伤后进入旧库" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-012",
            WorldRules =
            {
                "知识库硬事实：黑曜钥匙只能开启旧锁和辨认旧库门牌，不能攻击、治愈、修复、腐蚀门禁或新增战斗能力。"
            },
            CharacterStates = { "沈砚：携带黑曜钥匙进入旧库" },
            HardContinuityFacts =
            {
                "主角姓名：沈砚",
                "下一章必须承接：黑曜钥匙只能开启旧锁，不能攻击或修复"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第十二章：旧库门牌
            老针替沈砚治愈后背擦伤。他随后把黑曜钥匙插入旧锁，钥匙只辨认出旧库门牌的编号。
            <chapter_changes>{"CharacterStateChanges":["沈砚由老针治疗后继续行动"],"ConflictProgress":["沈砚用黑曜钥匙开启旧锁"],"NewPlotPoints":["黑曜钥匙辨认出旧库门牌编号"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚进入旧库"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            """,
            ChangesJson = """{"CharacterStateChanges":["沈砚由老针治疗后继续行动"],"ConflictProgress":["沈砚用黑曜钥匙开启旧锁"],"NewPlotPoints":["黑曜钥匙辨认出旧库门牌编号"],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚进入旧库"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}""",
            HasChanges = true
        };

        var report = await engine.ValidateDraftAsync(run, context, draft);

        Assert.DoesNotContain(report.Issues, issue => issue.Contains("黑曜钥匙") && issue.Contains("能力边界"));
    }

    [Fact]
    public void RepairPrompt_IncludesRequiredContinuityChecklistForEveryGateIssue()
    {
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-004",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "承接上一章继续进入信息坟场" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-004",
            HardContinuityFacts =
            {
                "下一章必须承接：工会追踪者的持续追捕",
                "下一章必须承接：获取前往第二环的门票",
                "下一章必须承接：女子的身份和命运"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = "第四章草稿正文\n<chapter_changes>{}</chapter_changes>",
            RepairAttemptCount = 1
        };
        var report = new GenerationGateReport
        {
            Issues =
            {
                "核心连续性失败：未承接「工会追踪者的持续追捕」。",
                "核心连续性失败：未承接「获取前往第二环的门票」。",
                "核心连续性失败：未承接「女子的身份和命运」。"
            },
            RepairHints =
            {
                "开章或关键场景必须回应上一章结尾/必须承接项：工会追踪者的持续追捕",
                "开章或关键场景必须回应上一章结尾/必须承接项：获取前往第二环的门票",
                "开章或关键场景必须回应上一章结尾/必须承接项：女子的身份和命运"
            }
        };

        var prompt = CreatePromptBuilder().BuildRepairUserPrompt(run, context, draft, report);
        using var doc = JsonDocument.Parse(prompt);

        var checklist = doc.RootElement.GetProperty("requiredContinuityCarryChecklist")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();
        var acceptanceRules = doc.RootElement.GetProperty("repairAcceptanceRules")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();

        Assert.Contains(checklist, item => item != null && item.Contains("工会追踪者的持续追捕"));
        Assert.Contains(checklist, item => item != null && item.Contains("获取前往第二环的门票"));
        Assert.Contains(checklist, item => item != null && item.Contains("女子的身份和命运"));
        Assert.Contains(acceptanceRules, item => item != null && item.Contains("正文"));
        Assert.Contains(acceptanceRules, item => item != null && item.Contains("逐条"));
    }

    [Fact]
    public void RepairPrompt_TellsModelToRewriteSilverBadgeBoundaryWithoutNegatedForbiddenWords()
    {
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-007",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "追踪残留信标" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-007",
            WorldRules =
            {
                "知识库硬事实：银蓝邮徽只能辨认/溯源邮路，不能攻击或治愈，不能激活机关或新增战斗能力。"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = "沈砚让银蓝邮徽的信号静默功能再次激活。\n<chapter_changes>{}</chapter_changes>",
            RepairAttemptCount = 1
        };
        var report = new GenerationGateReport
        {
            Issues = { "知识库硬事实失败：银蓝邮徽能力边界被改写，正文出现「银蓝邮徽的信号静默功能再次激活」。" },
            RepairHints = { "修订正文：银蓝邮徽只能辨认/溯源邮路，可提示方向或残留信标。" }
        };

        var prompt = CreatePromptBuilder().BuildRepairUserPrompt(run, context, draft, report);
        using var doc = JsonDocument.Parse(prompt);

        var acceptanceRules = doc.RootElement.GetProperty("repairAcceptanceRules")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();

        Assert.Contains(acceptanceRules, item =>
            item != null &&
            item.Contains("不要保留") &&
            item.Contains("越权词") &&
            item.Contains("辨认邮路"));
    }

    [Fact]
    public void RepairPrompt_BansPackagedUpgradeWordingForLimitedUseTool()
    {
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-006",
            UserGoal = "修订银蓝邮徽越权问题"
        };
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
            DraftContent = "沈砚发现银蓝邮徽基础共鸣辨识单元获得强化。\n<chapter_changes>{}</chapter_changes>",
            RepairAttemptCount = 2
        };
        var report = new GenerationGateReport
        {
            Issues = { "知识库硬事实失败：银蓝邮徽能力边界被改写，正文出现「银蓝邮徽基础共鸣/辨识单元获得强化」。" },
            RepairHints = { "修订正文：银蓝邮徽只能辨认/溯源邮路和残留信标，不能强化或新增能力。" }
        };

        var prompt = CreatePromptBuilder().BuildRepairUserPrompt(run, context, draft, report);
        using var doc = JsonDocument.Parse(prompt);

        var acceptanceRules = doc.RootElement.GetProperty("repairAcceptanceRules")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();

        Assert.Contains(acceptanceRules, item =>
            item != null &&
            item.Contains("强化") &&
            item.Contains("升级") &&
            item.Contains("扩展") &&
            item.Contains("基础共鸣") &&
            item.Contains("辨识单元"));

        var policy = doc.RootElement.GetProperty("boundaryRewritePolicy");
        Assert.Contains("整句改写", policy.GetProperty("instruction").GetString());
        Assert.Contains("基础单元升级", policy.GetProperty("forbiddenPackaging").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("被动能力", policy.GetProperty("forbiddenPackaging").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("辨认邮路", policy.GetProperty("allowedOnlyExamples").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void BuildWritingUserPrompt_IncludesStructuredChapterDirective()
    {
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
                "系统状态：邮差系统只给出投递方向提示",
                "知识库硬事实：银蓝邮徽只能辨认/溯源邮路，不能攻击或治愈，不能激活机关或新增战斗能力。",
                "下一章必须承接：残留信标指向潮汐塔外层"
            },
            WorldRules = { "潮汐塔内的门禁只能被收件记录触发。" }
        };

        var prompt = CreatePromptBuilder().BuildWritingUserPrompt(run, context);
        using var doc = JsonDocument.Parse(prompt);
        var directive = doc.RootElement.GetProperty("chapterDirective");

        Assert.Equal("chapter-008", directive.GetProperty("chapterId").GetString());
        Assert.Contains("沈砚", directive.GetProperty("protagonistAnchor").GetString());
        Assert.Contains("负伤", directive.GetProperty("protagonistAnchor").GetString());
        Assert.Contains("残留信标指向潮汐塔外层", directive.GetProperty("mustCarry").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains(directive.GetProperty("knowledgeBoundaries").EnumerateArray().Select(x => x.GetString()),
            item => item != null && item.Contains("银蓝邮徽") && item.Contains("不能攻击"));
        Assert.Contains(directive.GetProperty("acceptanceRules").EnumerateArray().Select(x => x.GetString()),
            item => item != null && item.Contains("硬事实") && item.Contains("正文"));
    }

    [Fact]
    public void BuildWritingUserPrompt_MapsBoundKnowledgeIntoTypedDirectiveSlots()
    {
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-009",
            UserGoal = "继续写第九章，利用知识库素材推进旧邮路。",
            ChapterBrief = new ChapterCreativeBrief
            {
                RecommendedCandidateTitle = "旧邮路裂口",
                CoreIdea = "沈砚进入废弃分拣站"
            }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-009",
            ChapterBlueprints = { "沈砚抵达废弃分拣站，发现旧邮袋里的潮湿黑信。" },
            KnowledgeBindings =
            {
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "hardfact-1",
                    EntryType = "HardFact",
                    Title = "银蓝邮徽能力边界",
                    Content = "银蓝邮徽只能辨认被篡改邮路，不能攻击、治愈或升级。",
                    ProjectUsageStatus = "referenced"
                },
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "style-1",
                    EntryType = "Style",
                    Title = "废土邮路风格",
                    Content = "叙述要冷硬克制，环境细节带锈味、潮气和旧纸味。",
                    ProjectUsageStatus = "referenced"
                },
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "material-1",
                    EntryType = "Material",
                    Title = "废弃分拣站素材",
                    Content = "分拣站有倒塌轨道、湿透邮袋和还在滴水的旧蓝灯。",
                    ProjectUsageStatus = "imported"
                },
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "character-1",
                    EntryType = "Character",
                    Title = "沈砚行动原则",
                    Content = "沈砚优先保护幸存邮差，不会为了爽点主动牺牲路人。",
                    ProjectUsageStatus = "referenced"
                },
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "world-1",
                    EntryType = "WorldRule",
                    Title = "旧邮路规则",
                    Content = "旧邮路只在雨后十五分钟内显形。",
                    ProjectUsageStatus = "referenced"
                }
            }
        };

        var prompt = CreatePromptBuilder().BuildWritingUserPrompt(run, context);
        using var doc = JsonDocument.Parse(prompt);
        var directive = doc.RootElement.GetProperty("chapterDirective");

        Assert.Contains(directive.GetProperty("knowledgeBoundaries").EnumerateArray().Select(x => x.GetString()),
            item => item != null && item.Contains("银蓝邮徽能力边界") && item.Contains("不能攻击"));
        Assert.Contains(directive.GetProperty("styleGuides").EnumerateArray().Select(x => x.GetString()),
            item => item != null && item.Contains("冷硬克制") && item.Contains("旧纸味"));
        Assert.Contains(directive.GetProperty("sceneMaterials").EnumerateArray().Select(x => x.GetString()),
            item => item != null && item.Contains("倒塌轨道") && item.Contains("湿透邮袋"));
        Assert.Contains(directive.GetProperty("characterKnowledge").EnumerateArray().Select(x => x.GetString()),
            item => item != null && item.Contains("保护幸存邮差"));
        Assert.Contains(directive.GetProperty("worldKnowledge").EnumerateArray().Select(x => x.GetString()),
            item => item != null && item.Contains("雨后十五分钟"));
    }

    [Fact]
    public void NormalizeWritingMaxTokens_ReservesEnoughBudgetForLongChapterAndChanges()
    {
        var method = typeof(HardcoreWritingEngine).GetMethod(
            "NormalizeWritingMaxTokens",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var normalized = Assert.IsType<int>(method!.Invoke(null, new object[] { 4096 }));

        Assert.True(normalized >= 16384);
    }

    [Fact]
    public void NormalizeChangesOnlyRepairMaxTokens_UsesSmallBudgetForProtocolPatch()
    {
        var method = typeof(HardcoreWritingEngine).GetMethod(
            "NormalizeChangesOnlyRepairMaxTokens",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var normalized = Assert.IsType<int>(method!.Invoke(null, new object[] { 4096 }));

        Assert.InRange(normalized, 1024, 4096);
    }

    [Fact]
    public void BuildChangesOnlyRepairUserPrompt_DoesNotAskModelToRewriteFullDraft()
    {
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

        var prompt = CreatePromptBuilder().BuildChangesOnlyRepairUserPrompt(run, context, draft, report);
        using var doc = JsonDocument.Parse(prompt);

        Assert.Equal("repair_chapter_changes_only", doc.RootElement.GetProperty("task").GetString());
        Assert.False(doc.RootElement.TryGetProperty("previousDraft", out _));
        Assert.Contains("沈砚抵达镜港", doc.RootElement.GetProperty("chapterBody").GetString());
        Assert.Contains("CharacterStateChanges", prompt);
    }

    [Fact]
    public async Task ChapterRewriter_UsesFullRepairWhenChangesIssueAlsoContainsKnowledgeHardFactFailure()
    {
        var rewriter = new ChapterRewriter(CreatePromptBuilder());
        var capturedUserPrompt = string.Empty;
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-001",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "沈砚进入断信区争夺旧邮路资源。" }
        };
        var context = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-001",
            WorldRules =
            {
                "知识库硬事实：银蓝邮徽只能辨认被篡改邮路，不能攻击、治疗或升级。"
            }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            沈砚解锁银蓝邮徽，邮徽获得新的战斗功能。
            <chapter_changes>{"CharacterStateChanges":[{"character":"沈砚"</chapter_changes>
            """,
            RepairAttemptCount = 1
        };
        var report = new GenerationGateReport
        {
            Issues =
            {
                "CHANGES段的JSON格式错误，请检查JSON语法；知识库硬事实失败：银蓝邮徽能力边界被改写，正文出现「解锁银蓝邮徽」。"
            },
            RepairHints =
            {
                "修订正文：银蓝邮徽只能辨认被篡改邮路，不能升级或新增战斗能力。",
                "修复 CHANGES JSON 语法。"
            }
        };

        await rewriter.RepairAsync(new ChapterRewriteRequest
        {
            Run = run,
            ContextPackage = context,
            Draft = draft,
            GateReport = report,
            CompleteAsync = (_, user, _) =>
            {
                capturedUserPrompt = user;
                return Task.FromResult($"""
                沈砚按住银蓝邮徽，徽面只短暂发亮，帮他辨认出被篡改的旧邮路。

                <chapter_changes>
                {MinimalChangesJson}
                </chapter_changes>
                """);
            }
        });

        using var doc = JsonDocument.Parse(capturedUserPrompt);
        Assert.Equal("draft_repair", doc.RootElement.GetProperty("task").GetString());
        Assert.True(doc.RootElement.TryGetProperty("previousDraft", out _));
        Assert.Contains("银蓝邮徽只能辨认", capturedUserPrompt);
        Assert.DoesNotContain("repair_chapter_changes_only", capturedUserPrompt);
    }

    [Fact]
    public async Task ChapterRewriter_ChangesOnlyRepairFallsBackToValidMinimalChangesWhenModelReturnsNoJson()
    {
        var rewriter = new ChapterRewriter(CreatePromptBuilder());
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            ChapterBrief = new ChapterCreativeBrief { CoreIdea = "男主遭遇异兽围攻并完成第一次打怪升级。" }
        };
        var draft = new ChapterDraftArtifact
        {
            DraftContent = """
            第二章正文。

            林烬在裂谷边缘遭遇骨甲犬群，他借系统提示找到薄弱点，夺下兽核完成第一次强化。
            """,
            RepairAttemptCount = 1
        };
        var report = new GenerationGateReport
        {
            Issues =
            {
                "正文末尾缺少变更摘要：请在正文结尾用成对的 <chapter_changes>...</chapter_changes> 标签包裹合法 JSON 对象。"
            },
            RepairHints =
            {
                "补齐 CHANGES JSON。"
            }
        };

        var repaired = await rewriter.RepairAsync(new ChapterRewriteRequest
        {
            Run = run,
            ContextPackage = new ChapterContextPackageSummary { ChapterId = "chapter-002" },
            Draft = draft,
            GateReport = report,
            CompleteAsync = (_, _, _) => Task.FromResult("我会补齐修订记录，但这里没有给出 JSON。")
        });

        Assert.True(repaired.HasChanges);
        Assert.NotEmpty(repaired.ChangesJson);
        Assert.True(ChapterChangesText.TryDeserializeChanges(repaired.ChangesJson, out var changes));
        Assert.NotNull(changes);
        Assert.Contains("chapter_changes", repaired.DraftContent);
        Assert.Contains("第二章正文", repaired.DraftContent);
    }

    [Fact]
    public void ExtractChangesJson_UsesLastValidChangesObjectWhenXmlContainsExplanationAndCodeFence()
    {
        var content = $$"""
        第一章正文。

        <chapter_changes>
        下面先说明一个错误示例：
        ```json
        {"note":"这不是正式 CHANGES"}
        ```

        {{MinimalChangesJson}}
        </chapter_changes>
        """;

        var changesJson = ChapterChangesText.ExtractChangesJson(content);

        Assert.NotEmpty(changesJson);
        Assert.DoesNotContain("\"note\"", changesJson);
        using var doc = JsonDocument.Parse(changesJson);
        Assert.True(doc.RootElement.TryGetProperty("CharacterStateChanges", out _));
        Assert.True(doc.RootElement.TryGetProperty("DeadlineConstraintChanges", out _));
    }

    [Fact]
    public void ExtractChangesJson_RejectsMalformedClosingTag()
    {
        var content = """
        第四章正文。
        <chapter_changes>
        {"CharacterStateChanges":["沈砚获得雾障罗盘"],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}
        </chapter_changes
        """;

        var changesJson = ChapterChangesText.ExtractChangesJson(content);

        Assert.Equal(string.Empty, changesJson);
    }

    [Fact]
    public void StripChanges_RemovesModelReasoningCloseTagFromChapterBody()
    {
        var content = """
        沈砚举起撬棍，看见邮路幽影从楼梯口走下来。</think>

        <chapter_changes>{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":null,"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
        """;

        var stripped = ChapterChangesText.StripChanges(content);

        Assert.Contains("沈砚举起撬棍", stripped);
        Assert.DoesNotContain("</think>", stripped);
        Assert.DoesNotContain("chapter_changes", stripped);
    }

    private sealed class RecordingChapterGatekeeper : IChapterGatekeeper
    {
        public bool WasCalled { get; private set; }

        public void ApplyHardGates(
            GenerationGateReport report,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft)
        {
            WasCalled = true;
            report.Status = "gate_failed";
            report.Issues.Add("测试 Gatekeeper 已接管硬门禁。");
        }
    }

    private sealed class RecordingGeneratedContentService : IGeneratedContentService, IGeneratedChapterMetadataWriter
    {
        public string ChapterId { get; private set; } = string.Empty;
        public string Content { get; private set; } = string.Empty;
        public string? Title { get; private set; }

        public Task SaveChapterAsync(string chapterId, string content)
        {
            ChapterId = chapterId;
            Content = content;
            Title = null;
            return Task.CompletedTask;
        }

        public Task SaveChapterAsync(string chapterId, string content, string? title)
        {
            ChapterId = chapterId;
            Content = content;
            Title = title;
            return Task.CompletedTask;
        }

        public Task<string?> GetChapterAsync(string chapterId) => Task.FromResult<string?>(null);
        public Task<bool> DeleteChapterAsync(string chapterId) => Task.FromResult(false);
        public bool ChapterExists(string chapterId) => false;
        public Task<List<TM.Services.Modules.ProjectData.Models.Generated.ChapterInfo>> GetGeneratedChaptersAsync() => Task.FromResult(new List<TM.Services.Modules.ProjectData.Models.Generated.ChapterInfo>());
        public Task<bool> VolumeExistsAsync(int volumeNumber) => Task.FromResult(false);
        public Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId) => Task.FromResult("chapter-001");
    }

    private sealed class ConfiguredWritingSettingsManager
    {
        public Task<ConfiguredWritingSettings> LoadAsync(CancellationToken ct) =>
            Task.FromResult(new ConfiguredWritingSettings());
    }

    private sealed class ConfiguredWritingSettings
    {
        public string LlmProvider { get; set; } = "openai";
        public string LlmApiKey { get; set; } = "unit-test-key";
        public string LlmBaseUrl { get; set; } = "https://unit.test/v1";
        public string LlmModel { get; set; } = "unit-test-model";
        public double LlmTemperature { get; set; } = 0.1;
        public int LlmMaxTokens { get; set; } = 4096;
    }

    private sealed class RecordingChapterRewriter : IChapterRewriter
    {
        public bool WasCalled { get; private set; }
        public ChapterRewriteRequest? Request { get; private set; }

        public Task<ChapterDraftArtifact> RepairAsync(
            ChapterRewriteRequest request,
            CancellationToken ct = default)
        {
            WasCalled = true;
            Request = request;
            return Task.FromResult(new ChapterDraftArtifact
            {
                ChapterId = request.Run.TargetChapterId,
                ArtifactId = request.Draft.ArtifactId,
                DraftContent = "修订结果\n<chapter_changes>{}</chapter_changes>",
                RepairAttemptCount = request.Draft.RepairAttemptCount + 1,
                Status = "rewriter_result"
            });
        }
    }

    private sealed class RecordingChapterFactWriter : IChapterFactWriter
    {
        private readonly ChapterContinuityFacts _facts;

        public RecordingChapterFactWriter(ChapterContinuityFacts facts)
        {
            _facts = facts;
        }

        public bool WasCalled { get; private set; }

        public async Task<ChapterFactWriteResult> ExtractAndPersistAsync(
            ChapterFactWriteRequest request,
            CancellationToken ct = default)
        {
            WasCalled = true;
            Assert.DoesNotContain("chapter_changes", request.CommittedContent, StringComparison.OrdinalIgnoreCase);
            _facts.ChapterId = string.IsNullOrWhiteSpace(_facts.ChapterId)
                ? request.Run.TargetChapterId
                : _facts.ChapterId;
            _facts.SourceRunId = request.Run.RunId;
            if (request.PersistAsync != null)
                await request.PersistAsync(_facts, ct);

            return new ChapterFactWriteResult
            {
                Success = true,
                Message = "ok",
                Facts = _facts
            };
        }
    }

    private sealed class RecordingChapterFactPostCommitScheduler : IChapterFactPostCommitScheduler
    {
        public bool WasScheduled { get; private set; }
        public ChapterFactWriteRequest? Request { get; private set; }

        public Task ScheduleAsync(
            ChapterFactWriteRequest request,
            Action<ChapterFactWriteResult>? onCompleted = null,
            Action<Exception>? onFailed = null,
            CancellationToken ct = default)
        {
            WasScheduled = true;
            Request = request;
            return Task.CompletedTask;
        }
    }
}

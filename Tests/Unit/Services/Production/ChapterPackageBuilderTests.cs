using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterPackageBuilderTests
{
    [Fact]
    public void Build_MergesStoryBibleContinuityVolumeAndChapterBriefIntoProductionPackage()
    {
        IChapterPackageBuilder builder = new ChapterPackageBuilder();
        var run = new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            UserGoal = "继续写第二章",
            ChapterBrief = new ChapterCreativeBrief
            {
                VolumeBeatRole = "承接危机并打开第一场战斗",
                CoreIdea = "旧邮徽第一次指向逃生路线",
                ConflictMove = "追踪者逼近，怪物围攻车站",
                CharacterChoice = "沈砚选择救下陌生女孩",
                CostOrConsequence = "邮徽留下蓝磷灼痕",
                ForeshadowingAction = "邮路地图缺失第三站"
            },
            StoryState = new StoryStateSnapshot
            {
                WorldRules = { "邮徽只能识路，不能直接攻击" },
                CharacterStates = { "沈砚：刚获得银蓝邮徽" },
                ActiveConflicts = { "工会追踪者正在逼近" },
                ActiveForeshadowing = { "蓝磷骨光" },
                PreviousChapterSummary = "chapter-001: 沈砚在车站拾起银蓝邮徽。",
                RagSearchQueries = { "旧邮路 车站 蓝磷", "旧邮路 车站 蓝磷" }
            }
        };
        var document = new StoryBibleDocument
        {
            Constitution = new StoryCreativeConstitution
            {
                Genre = "末世",
                SubGenre = "打怪升级",
                WorldCoreRule = "旧邮路开启需要付出记忆代价",
                MainConflictEngine = "邮政工会追捕所有私启邮路者"
            },
            VolumeArcs =
            {
                new VolumeArcPlan
                {
                    VolumeId = "volume-001",
                    Title = "第一卷 旧邮路开站",
                    StartChapterId = "chapter-001",
                    EndChapterId = "chapter-006",
                    VolumePromise = "男主理解邮徽代价并完成第一次逃生",
                    ExitState = "沈砚确认邮路不是奖励，而是债务"
                },
                new VolumeArcPlan
                {
                    VolumeId = "volume-002",
                    Title = "第二卷 暂不应进入",
                    StartChapterId = "chapter-007",
                    EndChapterId = "chapter-012",
                    VolumePromise = "未来卷目标"
                }
            },
            ContinuityFacts =
            {
                new ChapterContinuityFacts
                {
                    ChapterId = "chapter-001",
                    ProtagonistName = "沈砚",
                    ProtagonistIdentity = "旧邮路误入者",
                    ProtagonistStatus = "左掌有蓝磷灼痕",
                    CurrentLocation = "旧车站",
                    SystemState = "银蓝邮徽只能识路",
                    EquipmentState = "银蓝邮徽",
                    KeyEvents = { "拾起银蓝邮徽", "工会追踪者出现" },
                    EndingState = "沈砚被迫逃进旧站台",
                    NextChapterMustCarry =
                    {
                        "工会追踪者继续追捕",
                        "邮徽不能直接攻击",
                        "上一章已提交：第一章：银蓝邮徽",
                        "上一章章节ID：project-1-chapter-001"
                    }
                },
                new ChapterContinuityFacts
                {
                    ChapterId = "chapter-003",
                    ProtagonistName = "未来错误事实",
                    EndingState = "不应进入第二章生产包"
                }
            },
            CharacterLedger =
            {
                new CharacterLedgerEntry
                {
                    CharacterName = "沈砚",
                    Role = "男主",
                    Status = CharacterLedgerStatus.Active,
                    Summary = "谨慎但会救人",
                    Importance = 10,
                    UpdatedAt = DateTime.Now
                },
                new CharacterLedgerEntry
                {
                    CharacterName = "废弃角色",
                    Role = "反例",
                    Status = CharacterLedgerStatus.Rejected,
                    Summary = "不应进入生产包",
                    Importance = 99
                }
            },
            CanonLedger =
            {
                new CanonLedgerEntry
                {
                    Id = "knowledge-classification:badge-rule",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Canon,
                    Title = "知识规则：银蓝邮徽能力边界",
                    Content = "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
                    Rationale = "由知识库 LLM 分类提升。",
                    ImpactScope = "ProjectWide",
                    SourceRunId = "run-knowledge-classification",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                },
                new CanonLedgerEntry
                {
                    Id = "draft-canon-entry",
                    Type = CanonLedgerEntryType.WorldRule,
                    Status = CanonLedgerEntryStatus.Draft,
                    Title = "草稿规则",
                    Content = "草稿 Canon 不应进入生产包。"
                },
                new CanonLedgerEntry
                {
                    Id = "rejected-canon-entry",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Rejected,
                    Title = "已拒绝规则",
                    Content = "已拒绝 Canon 不应进入生产包。"
                }
            }
        };
        var contentContext = new ContentTaskContext
        {
            ChapterId = "chapter-002",
            ContextMode = ContentContextMode.Full,
            WorldRules =
            {
                new WorldRulesData
                {
                    Name = "旧邮路硬规则",
                    OneLineSummary = "旧邮路开启需要付出记忆代价"
                }
            },
            Characters =
            {
                new CharacterRulesData
                {
                    Name = "沈砚",
                    CharacterType = "主角",
                    Identity = "旧邮路误入者"
                }
            },
            Blueprints =
            {
                new BlueprintData
                {
                    Name = "第二章蓝图",
                    ChapterId = "chapter-002",
                    OneLineStructure = "怪物围攻车站，银蓝邮徽只能指路逃生"
                }
            },
            PreviousChapterSummaries =
            {
                new ChapterSummaryEntry
                {
                    ChapterId = "chapter-001",
                    Summary = "沈砚在车站拾起银蓝邮徽，工会追踪者出现。"
                }
            },
            LongDistanceRecallFragments =
            {
                new LongDistanceRecallFragment
                {
                    ChapterId = "chapter-001",
                    Content = "蓝磷骨光只表示旧邮路被唤醒，不是攻击能力。",
                    Score = 0.92
                }
            }
        };

        var package = builder.Build(new ChapterPackageBuildRequest
        {
            Run = run,
            Document = document,
            StoryState = run.StoryState,
            ContentContext = contentContext
        });

        Assert.Equal("chapter-002", package.ChapterId);
        Assert.Equal("context_ready:real_project_data", package.Status);
        Assert.Contains(package.WorldRules, line => line.Contains("旧邮路硬规则", StringComparison.Ordinal));
        Assert.Contains(package.WorldRules, line => line.Contains("Story Bible：末世/打怪升级", StringComparison.Ordinal));
        Assert.Contains(package.WorldRules, line => line.Contains("当前卷目标：第一卷 旧邮路开站", StringComparison.Ordinal));
        Assert.Contains(package.WorldRules, line =>
            line.Contains("Story Bible Canon：知识规则：银蓝邮徽能力边界", StringComparison.Ordinal) &&
            line.Contains("不能攻击、不能修复、不能升级", StringComparison.Ordinal));
        Assert.DoesNotContain(package.WorldRules, line => line.Contains("草稿 Canon 不应进入", StringComparison.Ordinal));
        Assert.DoesNotContain(package.WorldRules, line => line.Contains("已拒绝 Canon 不应进入", StringComparison.Ordinal));
        Assert.DoesNotContain(package.WorldRules, line => line.Contains("第二卷 暂不应进入", StringComparison.Ordinal));
        Assert.Contains(package.ActiveConflicts, line => line.Contains("主冲突引擎：邮政工会追捕", StringComparison.Ordinal));
        Assert.Contains(package.ActiveConflicts, line => line.Contains("卷出口状态目标：沈砚确认邮路不是奖励", StringComparison.Ordinal));
        Assert.Contains(package.CharacterStates, line => line.Contains("沈砚：旧邮路误入者", StringComparison.Ordinal));
        Assert.DoesNotContain(package.CharacterStates, line => line.Contains("废弃角色", StringComparison.Ordinal));
        Assert.Contains("主角姓名：沈砚", package.HardContinuityFacts);
        Assert.Contains("系统状态：银蓝邮徽只能识路", package.HardContinuityFacts);
        Assert.Contains("下一章必须承接：工会追踪者继续追捕", package.HardContinuityFacts);
        Assert.DoesNotContain(package.HardContinuityFacts, line => line.Contains("未来错误事实", StringComparison.Ordinal));
        Assert.DoesNotContain(package.HardContinuityFacts, line => line.Contains("上一章已提交", StringComparison.Ordinal));
        Assert.DoesNotContain(package.HardContinuityFacts, line => line.Contains("上一章章节ID", StringComparison.Ordinal));
        Assert.Contains(package.PreviousSummaries, line => line.Contains("chapter-001: 沈砚被迫逃进旧站台", StringComparison.Ordinal));
        Assert.Contains(package.LongDistanceRecall, line => line.Contains("蓝磷骨光只表示旧邮路被唤醒", StringComparison.Ordinal));
        Assert.Contains("卷节拍：承接危机并打开第一场战斗", package.ChapterBlueprints);
        Assert.Contains(package.ChapterBlueprints, line => line.Contains("怪物围攻车站", StringComparison.Ordinal));
        Assert.Contains("核心创意：旧邮徽第一次指向逃生路线", package.ChapterBlueprints);
        Assert.Single(package.RagQueries);
    }

    [Fact]
    public void Build_ProjectScopedFirstChapterIdDoesNotTreatChapter001FactsAsPreviousChapter()
    {
        IChapterPackageBuilder builder = new ChapterPackageBuilder();
        var run = new NovelAgentRun
        {
            TargetChapterId = "project-1-chapter-001",
            UserGoal = "写第一章",
            ChapterBrief = new ChapterCreativeBrief
            {
                CoreIdea = "第一章开启旧邮路。"
            }
        };
        var document = new StoryBibleDocument
        {
            VolumeArcs =
            {
                new VolumeArcPlan
                {
                    VolumeId = "volume-001",
                    Title = "第一卷 旧邮路开站",
                    StartChapterId = "chapter-001",
                    EndChapterId = "chapter-006",
                    VolumePromise = "男主第一次理解旧邮路代价"
                }
            },
            ContinuityFacts =
            {
                new ChapterContinuityFacts
                {
                    ChapterId = "chapter-001",
                    ProtagonistName = "旧章误入者",
                    EndingState = "这是同章事实，不能作为第一章的上一章结尾。",
                    NextChapterMustCarry = { "这条承接项不应进入第一章生产包" }
                }
            }
        };
        var contentContext = new ContentTaskContext
        {
            ChapterId = "project-1-chapter-001",
            ContextMode = ContentContextMode.Full,
            Blueprints =
            {
                new BlueprintData
                {
                    Name = "第一章蓝图",
                    ChapterId = "project-1-chapter-001",
                    OneLineStructure = "开场让沈砚发现旧邮路。"
                }
            }
        };

        var package = builder.Build(new ChapterPackageBuildRequest
        {
            Run = run,
            Document = document,
            StoryState = new StoryStateSnapshot(),
            ContentContext = contentContext
        });

        Assert.Contains(package.WorldRules, line => line.Contains("当前卷目标：第一卷 旧邮路开站", StringComparison.Ordinal));
        Assert.DoesNotContain(package.HardContinuityFacts, line => line.Contains("旧章误入者", StringComparison.Ordinal));
        Assert.DoesNotContain(package.HardContinuityFacts, line => line.Contains("上一章结尾状态", StringComparison.Ordinal));
        Assert.DoesNotContain(package.HardContinuityFacts, line => line.Contains("这条承接项不应进入第一章生产包", StringComparison.Ordinal));
        Assert.DoesNotContain(package.PreviousSummaries, line => line.Contains("这是同章事实", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ThrowsWhenRealContentContextIsMissing()
    {
        IChapterPackageBuilder builder = new ChapterPackageBuilder();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build(new ChapterPackageBuildRequest
        {
            Run = new NovelAgentRun { TargetChapterId = "chapter-002" },
            Document = new StoryBibleDocument(),
            StoryState = new StoryStateSnapshot()
        }));

        Assert.Contains("ContentTaskContext", ex.Message, StringComparison.Ordinal);
        Assert.Contains("真实项目上下文", ex.Message, StringComparison.Ordinal);
    }
}

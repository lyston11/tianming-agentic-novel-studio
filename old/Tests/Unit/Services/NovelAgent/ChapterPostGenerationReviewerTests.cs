using Moq;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;
using Xunit;

namespace Tests.Unit.Services.NovelAgent;

public class ChapterPostGenerationReviewerTests
{
    [Fact]
    public async Task ReviewAsync_WarnsWhenBoundProjectKnowledgeIsNotReflectedInChapter()
    {
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-002", """
            第二章：黑雨维修站

            沈砚沿着塌陷的街道冲出维修站，异兽群从雨幕里扑来。他靠速度绕开追击，在一间空仓库里喘息，确认黑雨会改变异兽嗅觉规则，随后决定继续寻找下一处邮路线索。
            """),
            new PassingUnifiedValidationService(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
            CreateSnapshotService());

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            ContextPackage = new ChapterContextPackageSummary
            {
                ChapterId = "chapter-002",
                KnowledgeBindings =
                {
                    new BoundKnowledgeSnapshot
                    {
                        KnowledgeId = "knowledge-old-route-cost",
                        EntryType = "HardFact",
                        Title = "旧邮路代价",
                        Content = "旧邮路每次开启都必须付出一段真实记忆作为通行费。",
                        ProjectUsageStatus = "referenced",
                        ProjectUsageCount = 2
                    }
                }
            }
        });

        var knowledgeCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "project_knowledge_binding_alignment", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Warning, knowledgeCheck.Status);
        Assert.Contains(knowledgeCheck.Evidence, evidence => evidence.Contains("旧邮路代价", StringComparison.Ordinal));
        Assert.True(review.RequiresRewrite);
    }

    [Fact]
    public async Task ReviewAsync_PassesWhenBoundProjectKnowledgeIsReflectedInChapter()
    {
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-002", """
            第二章：旧邮路代价

            沈砚没有立刻开启旧邮路。他知道旧邮路每次开启都必须付出一段真实记忆作为通行费，于是先把母亲留下的邮袋绑紧，才让银蓝邮徽贴上门牌。
            """),
            new PassingUnifiedValidationService(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
            CreateSnapshotService());

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            ContextPackage = new ChapterContextPackageSummary
            {
                ChapterId = "chapter-002",
                KnowledgeBindings =
                {
                    new BoundKnowledgeSnapshot
                    {
                        KnowledgeId = "knowledge-old-route-cost",
                        EntryType = "HardFact",
                        Title = "旧邮路代价",
                        Content = "旧邮路每次开启都必须付出一段真实记忆作为通行费。",
                        ProjectUsageStatus = "referenced",
                        ProjectUsageCount = 2
                    }
                }
            }
        });

        var knowledgeCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "project_knowledge_binding_alignment", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Pass, knowledgeCheck.Status);
        Assert.Contains(knowledgeCheck.Evidence, evidence => evidence.Contains("旧邮路代价", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReviewAsync_FailsWhenSourceRevisionPlanRequirementsAreNotReflectedInChapter()
    {
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-002", """
            第二章：维修站雨夜

            沈砚躲在维修站里整理背包。他听见远处传来雨声，决定等天亮以后再继续寻找旧邮路。
            """),
            new PassingUnifiedValidationService(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
            CreateSnapshotService());

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            ContextPackage = new ChapterContextPackageSummary
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
                        Status = "ready_for_rebuild",
                        RequirementsJson = "[\"怪物围攻\", \"打怪升级\"]",
                        ContinuityRequirementsJson = "[\"承接第一章银蓝邮徽刚到手\", \"银蓝邮徽不能攻击\"]",
                        Recommendation = "按修订计划重写第二章。"
                    }
                }
            }
        });

        var revisionPlanCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "revision_plan_alignment", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Fail, revisionPlanCheck.Status);
        Assert.Contains(revisionPlanCheck.Evidence, evidence => evidence.Contains("怪物围攻", StringComparison.Ordinal));
        Assert.True(review.RequiresRewrite);
    }

    [Fact]
    public async Task ReviewAsync_ProjectsEditorialDecisionFieldsOntoReviewSummary()
    {
        var editorialModel = new FakeEditorialReviewModelClient(new AgentEditorialReviewDecision
        {
            MeetsUserIntent = true,
            MeetsRevisionPlan = true,
            MeetsProjectPromise = true,
            MeetsAcceptedCreativeIntents = false,
            ContinuityRisk = "medium",
            CreativeFit = "medium",
            ChapterPacing = "needs_revision",
            Decision = "revise_before_commit",
            RecommendedAction = "revise_before_commit",
            Problems = { "怪物围攻创意落点偏弱" },
            Suggestions = { "提交前补强战斗反馈" },
            Evidence = { "第二章只写了逃跑，没有形成升级反馈" }
        });
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-002", """
            第二章：旧站台追击

            沈砚握着银蓝邮徽逃进站台，避开追来的怪物，却没有真正完成反击。
            """),
            new PassingUnifiedValidationService(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
            CreateSnapshotService(),
            editorialReviewModel: editorialModel,
            userId: "user-1");

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            RunId = "run-2",
            UserGoal = "第二章要落实怪物围攻和打怪升级反馈。",
            TargetChapterId = "chapter-002",
            ContextPackage = new ChapterContextPackageSummary
            {
                ChapterId = "chapter-002",
                AcceptedCreativeIntents =
                {
                    new AcceptedCreativeIntentSnapshot
                    {
                        IntentId = "intent-1",
                        NormalizedIntent = "第二章加入怪物围攻并给出升级反馈",
                        TargetScope = "chapter",
                        TargetChapterId = "chapter-002"
                    }
                }
            }
        });

        Assert.False(GetReviewProperty<bool>(review, "MeetsAcceptedCreativeIntents"));
        Assert.Equal("medium", GetReviewProperty<string>(review, "ContinuityRisk"));
        Assert.Equal("needs_revision", GetReviewProperty<string>(review, "ChapterPacing"));
        Assert.Equal("revise_before_commit", GetReviewProperty<string>(review, "RecommendedAction"));
    }

    [Fact]
    public async Task ReviewAsync_PassesWhenSourceRevisionPlanRequirementsAreReflectedInChapter()
    {
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-002", """
            第二章：怪物围攻

            沈砚刚把银蓝邮徽握进掌心，维修站外的怪物围攻就压了上来。银蓝邮徽不能攻击，也不能被当成武器，只能辨认旧邮路的逃生方向；他靠短刀和体力完成第一次打怪升级。
            """),
            new PassingUnifiedValidationService(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
            CreateSnapshotService());

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            ContextPackage = new ChapterContextPackageSummary
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
                        Status = "ready_for_rebuild",
                        RequirementsJson = "[\"怪物围攻\", \"打怪升级\"]",
                        ContinuityRequirementsJson = "[\"银蓝邮徽\", \"不能攻击\"]",
                        Recommendation = "按修订计划重写第二章。"
                    }
                }
            }
        });

        var revisionPlanCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "revision_plan_alignment", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Pass, revisionPlanCheck.Status);
        Assert.Contains(revisionPlanCheck.Evidence, evidence => evidence.Contains("revision-plan-002", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReviewAsync_PassesWhenSourceRevisionPlanUsesEquivalentAbilityBoundary()
    {
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-001", """
            第一章：旧邮路的蓝光

            沈砚在废城邮局醒来时，巡游怪物撞塌投递窗口。他没有把银蓝邮徽当成攻击武器，而是借蓝光看见旧邮路夹缝。
            章末他确认：银蓝邮徽只能指路，不能替他杀敌。
            """),
            new PassingUnifiedValidationService(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
            CreateSnapshotService());

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            TargetChapterId = "chapter-001",
            ContextPackage = new ChapterContextPackageSummary
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
                        Status = "ready_for_rebuild",
                        RequirementsJson = "[\"怪物围攻\", \"保留沈砚和银蓝邮徽\", \"邮徽不能主动攻击\"]"
                    }
                }
            }
        });

        var revisionPlanCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "revision_plan_alignment", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Pass, revisionPlanCheck.Status);
    }

    [Fact]
    public async Task ReviewAsync_UsesEditorialModelToValidateAcceptedCreativeIntentSemantics()
    {
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-002", """
            第二章：维修站雨夜

            沈砚躲在维修站里整理背包。他听见远处传来雨声，决定等天亮以后再继续寻找旧邮路。
            """),
            new PassingUnifiedValidationService(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
            CreateSnapshotService(),
            editorialReviewModel: new FakeEditorialReviewModelClient(new AgentEditorialReviewDecision
            {
                MeetsUserIntent = false,
                MeetsRevisionPlan = false,
                MeetsProjectPromise = true,
                CreativeFit = "low",
                Decision = "rewrite",
                Problems = { "用户要求第二章打怪升级，但正文仍是躲藏等待。" },
                Suggestions = { "重写为怪物围攻，让男主用银蓝邮徽找到逃生路线。" },
                Evidence = { "正文没有怪物围攻、打怪升级、邮徽逃生路线。" }
            }));

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            TargetChapterId = "chapter-002",
            ContextPackage = new ChapterContextPackageSummary
            {
                ChapterId = "chapter-002",
                AcceptedCreativeIntents =
                {
                    new AcceptedCreativeIntentSnapshot
                    {
                        IntentId = "intent-fight-upgrade",
                        NormalizedIntent = "第二章改成打怪升级，男主用邮徽逃生",
                        TargetScope = "chapter",
                        TargetChapterId = "chapter-002",
                        ImpactLevel = "chapter_rewrite",
                        Source = "chat"
                    }
                },
                SourceRevisionPlans =
                {
                    new RevisionPlanSnapshot
                    {
                        RevisionPlanId = "revision-plan-002",
                        PlanType = "chapter_rewrite",
                        TargetScope = "chapter",
                        TargetChapterId = "chapter-002",
                        RequirementsJson = "[\"怪物围攻\", \"邮徽逃生路线\"]",
                        Recommendation = "把第二章重写为打怪升级。"
                    }
                }
            }
        });

        var semanticCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "agent_editorial_semantic_alignment", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Fail, semanticCheck.Status);
        Assert.Contains("打怪升级", semanticCheck.Message);
        Assert.Contains(semanticCheck.Suggestions, suggestion => suggestion.Contains("邮徽", StringComparison.Ordinal));
        Assert.True(review.RequiresRewrite);
    }

    [Fact]
    public async Task ReviewAsync_UsesEditorialModelForUserGoalBriefAndProjectPromiseWithoutAcceptedIntent()
    {
        var storyStore = new InMemoryStoryBibleDocumentStore();
        var storyBible = new StoryBibleService(storyStore);
        await storyBible.CommitConstitutionAsync(new StoryCreativeConstitution
        {
            Genre = "末世废土",
            ReaderPromise = "打怪升级、资源争夺和基地成长",
            MainPleasure = "高压战斗后的明确成长反馈",
            CoreHook = "旧邮徽只能辨认危险路线，主角必须靠判断打出升级反馈"
        }, macroCandidates: null, sourceRunId: "run-foundation", overwrite: true, confirmed: true);
        var editorialModel = new FakeEditorialReviewModelClient(new AgentEditorialReviewDecision
        {
            MeetsUserIntent = false,
            MeetsRevisionPlan = true,
            MeetsProjectPromise = false,
            CreativeFit = "low",
            Decision = "revise_before_commit",
            Problems = { "用户目标要求打怪升级，但正文没有战斗与成长反馈。" },
            Suggestions = { "把本章改成异兽围攻，并给出明确升级结果。" },
            Evidence = { "正文以整理背包和等待为主。" }
        });
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-002", """
            第二章：雨夜暂歇

            陈默留在维修站里整理背包，等黑雨小一点再出发。
            """),
            new PassingUnifiedValidationService(),
            storyBible,
            CreateSnapshotService(),
            editorialReviewModel: editorialModel,
            userId: "user-1");

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            RunId = "run-goal-brief-review",
            TargetChapterId = "chapter-002",
            UserGoal = "第二章必须是打怪升级，不要再写等待和情绪拉扯。",
            ChapterBrief = new ChapterCreativeBrief
            {
                CoreIdea = "怪物围攻维修站，陈默靠邮徽辨认逃生路线并获得成长反馈。",
                ConflictMove = "从等待黑雨变成主动突围。",
                CostOrConsequence = "左臂伤势加重，但获得下一处旧邮路坐标。"
            },
            ContextPackage = new ChapterContextPackageSummary
            {
                ChapterId = "chapter-002"
            }
        });

        var semanticCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "agent_editorial_semantic_alignment", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Fail, semanticCheck.Status);
        Assert.Contains("打怪升级", semanticCheck.Message);
        Assert.NotNull(editorialModel.LastRequest);
        Assert.Equal("user-1", editorialModel.LastRequest!.UserId);
        Assert.Equal("打怪升级、资源争夺和基地成长", editorialModel.LastRequest.StoryConstitution?.ReaderPromise);
        Assert.Contains("打怪升级", editorialModel.LastRequest.UserGoal);
        Assert.True(review.RequiresRewrite);
    }

    [Fact]
    public async Task ReviewAsync_TreatsUnavailableUnifiedValidationAsFailure()
    {
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-001", """
            第一章：旧邮路

            沈砚握住银蓝邮徽，在废弃邮局里听见墙后的脚步声。他没有立刻冲出去，而是把邮徽贴近门缝，确认蓝磷骨光沿着旧邮路地图亮起。
            """),
            new ThrowingUnifiedValidationService(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
            CreateSnapshotService());

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            TargetChapterId = "chapter-001",
            DraftArtifact = new ChapterDraftArtifact
            {
                CommittedContent = "备用正文"
            }
        });

        var validationCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "unified_validation", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Fail, validationCheck.Status);
        Assert.True(review.RequiresRewrite);
        Assert.Equal("Fail", review.OverallResult);
    }

    [Fact]
    public async Task ReviewAsync_ReviewsDraftCandidateBeforeStoredChapterWhenRebuildingExistingChapter()
    {
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-001", """
            第一章：旧版逃生

            沈砚在废城邮局醒来，只沿着银蓝邮徽照出的窄巷逃走。
            """),
            new PreCommitStorageOnlyValidationService(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
            CreateSnapshotService());

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            TargetChapterId = "chapter-001",
            DraftArtifact = new ChapterDraftArtifact
            {
                DraftContent = """
                第一章：旧邮路的蓝光

                RevisionPlanRebuildE2E 按修订计划改写。沈砚在废城邮局醒来时，巡游怪物撞塌投递窗口。
                他没有把银蓝邮徽当成攻击武器，而是借蓝光看见旧邮路夹缝，诱使怪物撞进废弃邮柜，完成一次明确的生存反击。
                胜利不是力量暴涨，而是边界确认：银蓝邮徽只能指路，不能替他杀敌。章末，废弃分拣台里传出第二次敲击声。
                """
            },
            ContextPackage = new ChapterContextPackageSummary
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
                        Status = "ready_for_rebuild",
                        RequirementsJson = "[\"RevisionPlanRebuildE2E：把第一章改成怪物围攻后的明确生存反击\", \"保留沈砚和银蓝邮徽\", \"邮徽不能主动攻击\"]",
                        ContinuityRequirementsJson = "[\"章末仍要留下分拣台第二次敲击声\"]"
                    }
                }
            }
        });

        var validationCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "unified_validation", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Pass, validationCheck.Status);
        Assert.Contains("本轮候选正文", validationCheck.Message);

        var revisionPlanCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "revision_plan_alignment", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Pass, revisionPlanCheck.Status);
    }

    [Fact]
    public async Task ReviewAsync_TreatsPreCommitStorageValidationAsNonBlockingWhenDraftExists()
    {
        var reviewer = new ChapterPostGenerationReviewer(
            new InMemoryGeneratedContentService("chapter-001", ""),
            new PreCommitStorageOnlyValidationService(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
            CreateSnapshotService());

        var review = await reviewer.ReviewAsync(new NovelAgentRun
        {
            TargetChapterId = "chapter-001",
            DraftArtifact = new ChapterDraftArtifact
            {
                DraftContent = """
                第一章：邮徽醒来

                沈砚在废弃邮局外被异兽逼到墙角，银蓝邮徽贴上锈蚀门牌，只辨认出一条旧邮路的残留方向。他没有获得攻击或治愈能力，只能靠短刀、体力和判断冲出包围。
                """
            }
        });

        var validationCheck = Assert.Single(
            review.Checks,
            check => string.Equals(check.Key, "unified_validation", StringComparison.Ordinal));
        Assert.Equal(NovelAgentReviewCheckStatus.Pass, validationCheck.Status);
        Assert.Contains("提交前草稿评审", validationCheck.Message);
    }

    private static StoryStateSnapshotService CreateSnapshotService() =>
        new(
            Mock.Of<IGuideContextService>(),
            Mock.Of<IContentChunkSearchService>(),
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()));

    private static T? GetReviewProperty<T>(NovelAgentPostGenerationReview review, string propertyName)
    {
        var property = typeof(NovelAgentPostGenerationReview).GetProperty(propertyName);
        Assert.NotNull(property);
        return (T?)property!.GetValue(review);
    }

    private sealed class InMemoryGeneratedContentService : IGeneratedContentService
    {
        private readonly string _chapterId;
        private readonly string _content;

        public InMemoryGeneratedContentService(string chapterId, string content)
        {
            _chapterId = chapterId;
            _content = content;
        }

        public Task SaveChapterAsync(string chapterId, string content) => Task.CompletedTask;

        public Task<string?> GetChapterAsync(string chapterId) =>
            Task.FromResult<string?>(string.Equals(chapterId, _chapterId, StringComparison.OrdinalIgnoreCase)
                ? _content
                : null);

        public Task<bool> DeleteChapterAsync(string chapterId) => Task.FromResult(false);

        public bool ChapterExists(string chapterId) =>
            string.Equals(chapterId, _chapterId, StringComparison.OrdinalIgnoreCase);

        public Task<List<ChapterInfo>> GetGeneratedChaptersAsync() => Task.FromResult(new List<ChapterInfo>());

        public Task<bool> VolumeExistsAsync(int volumeNumber) => Task.FromResult(false);

        public Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId) =>
            Task.FromResult("chapter-002");
    }

    private sealed class ThrowingUnifiedValidationService : IUnifiedValidationService
    {
        public Task<ChapterValidationResult> ValidateChapterAsync(string chapterId, CancellationToken ct = default) =>
            throw new InvalidOperationException("统一校验服务未接入真实项目作用域。");

        public Task<VolumeValidationResult> ValidateVolumeAsync(int volumeNumber, CancellationToken ct = default) =>
            throw new InvalidOperationException("统一校验服务未接入真实项目作用域。");

        public Task<bool> NeedsRepublishAsync() =>
            throw new InvalidOperationException("统一校验服务未接入真实项目作用域。");
    }

    private sealed class PreCommitStorageOnlyValidationService : IUnifiedValidationService
    {
        public Task<ChapterValidationResult> ValidateChapterAsync(string chapterId, CancellationToken ct = default) =>
            Task.FromResult(new ChapterValidationResult
            {
                ChapterId = chapterId,
                OverallResult = "失败",
                IssuesByModule =
                {
                    ["chapter_identity"] = new List<ValidationIssue>
                    {
                        new()
                        {
                            Type = "chapter_identity",
                            Severity = "Error",
                            Message = $"项目中不存在章节 {chapterId}。"
                        }
                    }
                }
            });

        public Task<VolumeValidationResult> ValidateVolumeAsync(int volumeNumber, CancellationToken ct = default) =>
            Task.FromResult(new VolumeValidationResult { VolumeNumber = volumeNumber });

        public Task<bool> NeedsRepublishAsync() => Task.FromResult(false);
    }

    private sealed class PassingUnifiedValidationService : IUnifiedValidationService
    {
        public Task<ChapterValidationResult> ValidateChapterAsync(string chapterId, CancellationToken ct = default) =>
            Task.FromResult(new ChapterValidationResult
            {
                ChapterId = chapterId,
                OverallResult = "通过"
            });

        public Task<VolumeValidationResult> ValidateVolumeAsync(int volumeNumber, CancellationToken ct = default) =>
            Task.FromResult(new VolumeValidationResult
            {
                VolumeNumber = volumeNumber
            });

        public Task<bool> NeedsRepublishAsync() => Task.FromResult(false);
    }

    private sealed class FakeEditorialReviewModelClient : IAgentEditorialReviewModelClient
    {
        private readonly AgentEditorialReviewDecision _decision;

        public FakeEditorialReviewModelClient(AgentEditorialReviewDecision decision)
        {
            _decision = decision;
        }

        public Task<AgentEditorialReviewDecision> ReviewAsync(
            AgentEditorialReviewRequest request,
            CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(_decision);
        }

        public AgentEditorialReviewRequest? LastRequest { get; private set; }
    }
}

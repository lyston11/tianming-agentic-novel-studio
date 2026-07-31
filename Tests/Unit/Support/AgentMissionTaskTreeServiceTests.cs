using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class AgentMissionTaskTreeServiceTests
{
    [Fact]
    public void Sync_AllowsCommitStoryFoundationWhenFoundationCandidatesExist()
    {
        var session = new AgentSession
        {
            SessionId = "session-foundation",
            UserId = "user-001",
            ActiveProjectId = "project-001"
        };
        var project = new NovelProjectInfo
        {
            Id = "project-001",
            Title = "测试小说",
            CoreHook = "故事地基候选应可固化"
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = "foundation-run-001",
                    Intent = NovelAgentIntent.CreateStoryFoundation,
                    Status = NovelAgentRunStatus.Planning,
                    MacroCandidates =
                    {
                        new MacroStoryConceptCandidate
                        {
                            CandidateId = "foundation-candidate-001",
                            Title = "邮路溯源升级流",
                            CoreHook = "沈砚靠银蓝邮徽辨认旧邮路。"
                        }
                    }
                }
            }
        };

        new AgentMissionTaskTreeService().Sync(session, project, bible);

        Assert.Contains("CommitStoryFoundation", session.WorkingMemory.MissionPlan.AllowedNextActions);
        Assert.Contains("PlanStoryFoundation", session.WorkingMemory.MissionPlan.AllowedNextActions);
    }

    [Fact]
    public void Sync_AllowsCommitVolumeArcWhenDraftVolumePlanExists()
    {
        var session = new AgentSession
        {
            SessionId = "session-volume",
            UserId = "user-001",
            ActiveProjectId = "project-001"
        };
        var project = new NovelProjectInfo
        {
            Id = "project-001",
            Title = "测试小说",
            CoreHook = "卷规划候选应可提交"
        };
        var bible = new StoryBibleDocument
        {
            Constitution = new StoryCreativeConstitution { CoreHook = "测试钩子" },
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = "volume-run-001",
                    Intent = NovelAgentIntent.PlanVolumeArc,
                    Status = NovelAgentRunStatus.Planning,
                    VolumeArcPlan = new VolumeArcPlan
                    {
                        VolumeId = "volume-001",
                        Title = "第一卷：废土邮路升级流",
                        StartChapterId = "chapter-001",
                        EndChapterId = "chapter-008",
                        ExpectedChapterCount = 8
                    }
                }
            }
        };

        new AgentMissionTaskTreeService().Sync(session, project, bible);

        Assert.Contains("CommitVolumeArc", session.WorkingMemory.MissionPlan.AllowedNextActions);
        Assert.Contains("PlanVolumeArc", session.WorkingMemory.MissionPlan.AllowedNextActions);
    }

    [Fact]
    public void Sync_GateFailedChapterQueuesRepairInsteadOfBlocking()
    {
        var session = new AgentSession
        {
            SessionId = "session-001",
            UserId = "user-001",
            ActiveProjectId = "project-001"
        };
        var project = new NovelProjectInfo
        {
            Id = "project-001",
            Title = "测试小说",
            CoreHook = "门禁失败后应自动修复"
        };
        var bible = new StoryBibleDocument
        {
            Constitution = new StoryCreativeConstitution { CoreHook = "测试钩子" },
            VolumeArcs =
            {
                new VolumeArcPlan
                {
                    VolumeId = "volume-001",
                    Title = "第一卷",
                    StartChapterId = "chapter-001",
                    EndChapterId = "chapter-001",
                    ExpectedChapterCount = 1,
                    Status = VolumeArcStatus.Canon,
                    ChapterBeats = { new VolumeChapterBeat { Index = 1, Goal = "开局" } }
                }
            },
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = "run-001",
                    Intent = NovelAgentIntent.PlanChapter,
                    Status = NovelAgentRunStatus.Repairing,
                    TargetChapterId = "chapter-001",
                    ChapterBrief = new ChapterCreativeBrief
                    {
                        ChapterId = "chapter-001",
                        SelectedCandidateTitle = "目标推进"
                    },
                    ContextPackage = new ChapterContextPackageSummary
                    {
                        ChapterId = "chapter-001",
                        Status = "context_ready"
                    },
                    DraftArtifact = new ChapterDraftArtifact
                    {
                        ArtifactId = "draft-001",
                        ChapterId = "chapter-001",
                        Status = "draft_generated",
                        DraftContent = "正文\n<chapter_changes>{}</chapter_changes>",
                        HasChanges = true
                    },
                    GateReport = new GenerationGateReport
                    {
                        Status = "gate_failed",
                        Issues = { "未承接上一章追捕线" }
                    }
                }
            }
        };

        new AgentMissionTaskTreeService().Sync(session, project, bible);

        var task = Assert.Single(
            session.WorkingMemory.MissionPlan.SchedulerState.Tasks,
            item => item.RunId == "run-001");
        Assert.Equal("queued", task.Status);
        Assert.Equal("ProduceChapter", task.NextAction);

    }

    [Fact]
    public void Sync_GateValidatedWithoutQualityReview_QueuesReviewBeforeCommit()
    {
        var session = new AgentSession
        {
            SessionId = "session-001",
            UserId = "user-001",
            ActiveProjectId = "project-001",
            ActiveRunId = "run-001"
        };
        var project = new NovelProjectInfo
        {
            Id = "project-001",
            Title = "测试小说",
            CoreHook = "门禁通过后必须先质量评审"
        };
        var bible = new StoryBibleDocument
        {
            Constitution = new StoryCreativeConstitution { CoreHook = "测试钩子" },
            VolumeArcs =
            {
                new VolumeArcPlan
                {
                    VolumeId = "volume-001",
                    Title = "第一卷",
                    StartChapterId = "chapter-001",
                    EndChapterId = "chapter-001",
                    ExpectedChapterCount = 1,
                    Status = VolumeArcStatus.Canon,
                    ChapterBeats = { new VolumeChapterBeat { Index = 1, Goal = "开局" } }
                }
            },
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = "run-001",
                    Intent = NovelAgentIntent.PlanChapter,
                    Status = NovelAgentRunStatus.Planning,
                    TargetChapterId = "chapter-001",
                    ChapterBrief = new ChapterCreativeBrief
                    {
                        ChapterId = "chapter-001",
                        SelectedCandidateTitle = "目标推进"
                    },
                    ContextPackage = new ChapterContextPackageSummary
                    {
                        ChapterId = "chapter-001",
                        Status = "context_ready"
                    },
                    DraftArtifact = new ChapterDraftArtifact
                    {
                        ArtifactId = "draft-001",
                        ChapterId = "chapter-001",
                        Status = "draft_generated",
                        DraftContent = "正文",
                        HasChanges = true
                    },
                    GateReport = new GenerationGateReport
                    {
                        Status = "validated",
                        ProtocolPassed = true,
                        ChangesDetected = true,
                        FactSnapshotPassed = true,
                        BlueprintPassed = true,
                        RagPassed = true
                    }
                }
            }
        };

        new AgentMissionTaskTreeService().Sync(session, project, bible);

        var task = Assert.Single(
            session.WorkingMemory.MissionPlan.SchedulerState.Tasks,
            item => item.RunId == "run-001");
        Assert.Equal("ProduceChapter", task.NextAction);
        Assert.Equal("pending_quality_review", task.Reason);
        Assert.Contains("ProduceChapter", session.WorkingMemory.MissionPlan.AllowedNextActions);
        Assert.DoesNotContain("CommitValidatedChapter", session.WorkingMemory.MissionPlan.AllowedNextActions);
    }

    [Fact]
    public void Sync_RegeneratedDraftWithoutReviewClearsStaleQualityFailure()
    {
        var session = new AgentSession
        {
            SessionId = "session-001",
            UserId = "user-001",
            ActiveProjectId = "project-001",
            ActiveRunId = "run-001"
        };
        var project = new NovelProjectInfo
        {
            Id = "project-001",
            Title = "测试小说",
            CoreHook = "重新生成草稿后不能沿用旧质量失败"
        };
        var staleChapter = new AgentChapterTask
        {
            ChapterId = "chapter-001",
            RunId = "run-001",
            Status = "quality_failed",
            DraftStatus = "draft_generated",
            GateStatus = "validated",
            QualityStatus = "quality_failed",
            QualityIssueSummary = "章节正文为空，需重新生成或补写。",
            NextAction = "RepairChapterDraft"
        };
        session.WorkingMemory.MissionPlan.BookTaskTree.Volumes.Add(new AgentVolumeTask
        {
            VolumeId = "volume-001",
            Chapters = { staleChapter }
        });

        var bible = new StoryBibleDocument
        {
            Constitution = new StoryCreativeConstitution { CoreHook = "测试钩子" },
            VolumeArcs =
            {
                new VolumeArcPlan
                {
                    VolumeId = "volume-001",
                    Title = "第一卷",
                    StartChapterId = "chapter-001",
                    EndChapterId = "chapter-001",
                    ExpectedChapterCount = 1,
                    Status = VolumeArcStatus.Canon,
                    ChapterBeats = { new VolumeChapterBeat { Index = 1, Goal = "开局" } }
                }
            },
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = "run-001",
                    Intent = NovelAgentIntent.PlanChapter,
                    Status = NovelAgentRunStatus.Validating,
                    TargetChapterId = "chapter-001",
                    ChapterBrief = new ChapterCreativeBrief
                    {
                        ChapterId = "chapter-001",
                        SelectedCandidateTitle = "目标推进"
                    },
                    ContextPackage = new ChapterContextPackageSummary
                    {
                        ChapterId = "chapter-001",
                        Status = "context_ready"
                    },
                    DraftArtifact = new ChapterDraftArtifact
                    {
                        ArtifactId = "draft-001",
                        ChapterId = "chapter-001",
                        Status = "draft_generated",
                        DraftContent = "新生成的正文\n<chapter_changes>{}</chapter_changes>",
                        HasChanges = true
                    },
                    GateReport = null,
                    PostGenerationReview = null
                }
            }
        };

        new AgentMissionTaskTreeService().Sync(session, project, bible);

        var task = Assert.Single(
            session.WorkingMemory.MissionPlan.SchedulerState.Tasks,
            item => item.RunId == "run-001");
        var chapter = Assert.Single(session.WorkingMemory.MissionPlan.BookTaskTree.Volumes.SelectMany(v => v.Chapters));
        Assert.Equal("draft_generated", chapter.Status);
        Assert.Equal("not_reviewed", chapter.QualityStatus);
        Assert.Equal(string.Empty, chapter.QualityIssueSummary);
        Assert.Equal("ProduceChapter", task.NextAction);
        Assert.DoesNotContain("RepairChapterDraft", chapter.AllowedNextActions);
    }

    [Fact]
    public void Sync_UsesLatestRunWhenSameChapterHasStaleFailureAfterReviewedRun()
    {
        var session = new AgentSession
        {
            SessionId = "session-001",
            UserId = "user-001",
            ActiveProjectId = "project-001",
            ActiveRunId = "run-new"
        };
        var project = new NovelProjectInfo
        {
            Id = "project-001",
            Title = "测试小说",
            CoreHook = "同章节重试后不能被旧失败覆盖"
        };
        var bible = new StoryBibleDocument
        {
            Constitution = new StoryCreativeConstitution { CoreHook = "测试钩子" },
            VolumeArcs =
            {
                new VolumeArcPlan
                {
                    VolumeId = "volume-001",
                    Title = "第一卷",
                    StartChapterId = "chapter-001",
                    EndChapterId = "chapter-001",
                    ExpectedChapterCount = 1,
                    Status = VolumeArcStatus.Canon,
                    ChapterBeats = { new VolumeChapterBeat { Index = 1, Goal = "开局" } }
                }
            },
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = "run-new",
                    Intent = NovelAgentIntent.PlanChapter,
                    Status = NovelAgentRunStatus.Planning,
                    TargetChapterId = "chapter-001",
                    UpdatedAt = new DateTime(2026, 6, 19, 15, 2, 36),
                    ChapterBrief = new ChapterCreativeBrief
                    {
                        ChapterId = "chapter-001",
                        SelectedCandidateTitle = "新通过版本"
                    },
                    ContextPackage = new ChapterContextPackageSummary
                    {
                        ChapterId = "chapter-001",
                        Status = "context_ready"
                    },
                    DraftArtifact = new ChapterDraftArtifact
                    {
                        ArtifactId = "draft-new",
                        ChapterId = "chapter-001",
                        Status = "draft_generated",
                        DraftContent = "新正文\n<chapter_changes>{}</chapter_changes>",
                        HasChanges = true
                    },
                    GateReport = new GenerationGateReport
                    {
                        Status = "validated",
                        ProtocolPassed = true,
                        ChangesDetected = true,
                        FactSnapshotPassed = true,
                        BlueprintPassed = true,
                        RagPassed = true
                    },
                    PostGenerationReview = new NovelAgentPostGenerationReview
                    {
                        ChapterId = "chapter-001",
                        ReviewId = "review-new",
                        QualityScore = 88,
                        RequiresRewrite = false,
                        Summary = "评审通过"
                    }
                },
                new NovelAgentRun
                {
                    RunId = "run-old",
                    Intent = NovelAgentIntent.PlanChapter,
                    Status = NovelAgentRunStatus.Repairing,
                    TargetChapterId = "chapter-001",
                    UpdatedAt = new DateTime(2026, 6, 19, 14, 2, 36),
                    ChapterBrief = new ChapterCreativeBrief
                    {
                        ChapterId = "chapter-001",
                        SelectedCandidateTitle = "旧失败版本"
                    },
                    DraftArtifact = new ChapterDraftArtifact
                    {
                        ArtifactId = "draft-old",
                        ChapterId = "chapter-001",
                        Status = "draft_generated",
                        DraftContent = "旧正文",
                        HasChanges = false
                    },
                    GateReport = new GenerationGateReport
                    {
                        Status = "gate_failed",
                        Issues = { "CHANGES JSON 不是可解析对象。" }
                    }
                }
            }
        };

        new AgentMissionTaskTreeService().Sync(session, project, bible);

        var chapter = Assert.Single(session.WorkingMemory.MissionPlan.BookTaskTree.Volumes.SelectMany(v => v.Chapters));
        var task = Assert.Single(session.WorkingMemory.MissionPlan.SchedulerState.Tasks, item => item.ChapterId == "chapter-001");

        Assert.Equal("run-new", chapter.RunId);
        Assert.Equal("quality_passed", chapter.Status);
        Assert.Equal("ProduceChapter", chapter.NextAction);
        Assert.Equal("ProduceChapter", task.NextAction);
        Assert.Equal("run-new", task.RunId);
    }
}

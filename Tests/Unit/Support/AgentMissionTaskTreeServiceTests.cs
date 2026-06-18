using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class AgentMissionTaskTreeServiceTests
{
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
        Assert.Equal("ReviewChapter", task.NextAction);
        Assert.Equal("pending_quality_review", task.Reason);
        Assert.Contains("ReviewChapter", session.WorkingMemory.MissionPlan.AllowedNextActions);
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
                        DraftContent = "新生成的正文\n\n## CHANGES\n{}",
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
        Assert.Equal("ValidateChapterDraft", task.NextAction);
        Assert.DoesNotContain("RepairChapterDraft", chapter.AllowedNextActions);
    }
}

using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;
using System.Reflection;
using Xunit;

namespace Tests.Unit.Support;

public class ProjectWorkflowTests
{
    [Fact]
    public void BuildArtifactTimeline_DoesNotTreatWorkflowDraftAsLibraryChapter()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: true));

        var timeline = ProjectWorkflow.BuildArtifactTimeline(
            library,
            new StoryBibleDocument(),
            Array.Empty<WorkflowChapterArtifactSummary>(),
            Array.Empty<AgentScheduledTask>());

        Assert.DoesNotContain(timeline, item => item.Kind == "library_chapter");
        Assert.Contains(timeline, item => item.Kind == "volume_plan");
    }

    [Fact]
    public void BuildProductionStages_UsesCommittedChapterCountForLibraryStage()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: true));
        var timeline = ProjectWorkflow.BuildArtifactTimeline(
            library,
            new StoryBibleDocument(),
            Array.Empty<WorkflowChapterArtifactSummary>(),
            Array.Empty<AgentScheduledTask>());

        var stages = ProjectWorkflow.BuildProductionStages(library, timeline, Array.Empty<AgentScheduledTask>());
        var libraryStage = Assert.Single(stages, stage => stage.Key == "library");

        Assert.Equal("empty", libraryStage.Status);
        Assert.Equal(0, libraryStage.CurrentCount);
        Assert.Equal(6, libraryStage.TotalCount);
    }

    [Fact]
    public void NovelLibrary_DoesNotShowCommittedDraftWithoutQualityReview()
    {
        var method = typeof(NovelLibrary).GetMethod("IsLibraryVisible", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var run = new NovelAgentRun
        {
            GateReport = new GenerationGateReport { Status = "validated" },
            DraftArtifact = new ChapterDraftArtifact
            {
                Status = "committed",
                CommittedContent = "正文已提交但缺少质量评审"
            }
        };

        var visible = Assert.IsType<bool>(method!.Invoke(null, new object?[] { run }));

        Assert.False(visible);
    }

    [Fact]
    public void WebReviewer_UsesDraftContentWhenChapterDocumentIsNotCommittedYet()
    {
        var reviewerType = typeof(AgentRuntime).Assembly.GetType(
            "TM.Services.Framework.AI.NovelAgent.Services.ChapterPostGenerationReviewer");
        Assert.NotNull(reviewerType);

        var method = reviewerType!.GetMethod("ResolveReviewContent", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var draft = "第一章正文\n主角启动深海机甲。\n\n## CHANGES\n{\"facts\":[]}";
        var content = Assert.IsType<string>(method!.Invoke(null, new object?[] { "", "", draft }));

        Assert.Contains("主角启动深海机甲", content);
        Assert.DoesNotContain("CHANGES", content);
    }

    private static NovelLibraryDocument BuildLibrary(NovelChapterView chapter)
    {
        var volume = new NovelVolumeView(
            "volume-001",
            "第一卷",
            "Draft",
            chapter.ChapterId,
            chapter.ChapterId,
            6,
            new[] { chapter });
        var book = new NovelBookView(
            "project-001",
            "测试小说",
            "科幻",
            "机甲",
            "过程草稿不等于书城入库",
            string.Empty,
            "Drafting",
            true,
            1,
            1,
            6,
            0,
            DateTime.UtcNow.ToString("O"),
            chapter);

        return new NovelLibraryDocument(
            new[] { book },
            book,
            new[] { volume },
            chapter,
            1,
            6,
            0);
    }

    private static NovelChapterView Chapter(bool visibleInWorkflow, bool visibleInLibrary, bool hasGeneratedContent) =>
        new(
            "chapter-001",
            "目标推进",
            "volume-001",
            "第一卷",
            1,
            "开局",
            "建立困境",
            "发现机会",
            "付出代价",
            "validated",
            "run-001",
            "GenerateChapterDraft",
            DateTime.UtcNow.ToString("O"),
            hasGeneratedContent,
            false,
            0,
            "草稿已生成。",
            "草稿预览",
            "目标推进",
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            "validated",
            string.Empty,
            "draft_generated",
            "validated",
            true,
            true,
            true,
            false,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            visibleInWorkflow,
            visibleInLibrary,
            "硬门禁已通过",
            "validated",
            "draft:run-001",
            "gate:run-001",
            string.Empty);
}

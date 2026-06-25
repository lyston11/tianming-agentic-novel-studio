using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using Xunit;

namespace Tests.Unit.Services.AgentTools;

public sealed class AgentToolProgressPresenterHeartbeatTests
{
    [Fact]
    public void DescribeRunning_ForProduceChapterUsesClosedLoopWording()
    {
        var progress = AgentToolProgressPresenter.DescribeRunning(
            "ProduceChapter",
            "running",
            "writing",
            "run-000");

        Assert.Equal("正在推进章节生产闭环", progress.Title);
        Assert.DoesNotContain("生产并提交章节", progress.Title);
        Assert.Equal("创作工作流和小说书城", progress.ResultLocation);
        Assert.Contains("过程结果会进入工作流", progress.Detail);
    }

    [Fact]
    public void DescribeHeartbeat_ForChapterGenerationShowsElapsedAndNoNewArtifact()
    {
        var progress = AgentToolProgressPresenter.DescribeHeartbeat(
            NovelAgentProductionStages.DraftGeneration,
            TimeSpan.FromSeconds(95),
            "drafting",
            "run-001");

        Assert.Equal(NovelAgentProductionStages.DraftGeneration, progress.ToolName);
        Assert.Equal("running", progress.Status);
        Assert.True(progress.IsRunning);
        Assert.Equal("run-001", progress.RunId);
        Assert.Contains("正在生成章节草稿", progress.Title);
        Assert.Contains("仍在等待模型", progress.Detail);
        Assert.Contains("当前无新增产物", progress.Detail);
    }

    [Fact]
    public void DescribeHeartbeat_ForCommitShowsBookstoreStage()
    {
        var progress = AgentToolProgressPresenter.DescribeHeartbeat(
            NovelAgentProductionStages.ChapterCommit,
            TimeSpan.FromSeconds(20),
            "committing",
            "run-002");

        Assert.Contains("写入书城", progress.Detail);
        Assert.Contains("后台会继续刷新", progress.Detail);
    }
}

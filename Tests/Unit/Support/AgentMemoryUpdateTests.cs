using Xunit;
using TM.Web.NovelAgentWeb.Support;

namespace Tests.Unit.Support;

public class AgentMemoryUpdateTests
{
    [Fact]
    public void DefaultConstructor_InitializesAllMemoryLayers()
    {
        var update = new AgentMemoryUpdate();

        Assert.NotNull(update.SessionMemory);
        Assert.NotNull(update.ProjectMemory);
        Assert.NotNull(update.AuthorMemory);
        Assert.NotNull(update.ExecutionMemory);
    }

    [Fact]
    public void SessionMemoryUpdate_CanSetAllFields()
    {
        var sessionUpdate = new SessionMemoryUpdate
        {
            ChatSummary = "本轮对话完成了章节大纲构思",
            ExtractedPreferences = new List<string> { "保持节奏紧凑", "避免冗长环境描写" }
        };

        Assert.Equal("本轮对话完成了章节大纲构思", sessionUpdate.ChatSummary);
        Assert.Equal(2, sessionUpdate.ExtractedPreferences.Count);
    }

    [Fact]
    public void ProjectMemoryUpdate_CanSetAllFields()
    {
        var projectUpdate = new ProjectMemoryUpdate
        {
            NewConstraints = new List<string> { "不要出现现代科技元素" },
            UnresolvedThreads = new List<string> { "主角身世之谜（第15章揭示）" }
        };

        Assert.Single(projectUpdate.NewConstraints);
        Assert.Single(projectUpdate.UnresolvedThreads);
    }

    [Fact]
    public void AuthorMemoryUpdate_CanSetAllFields()
    {
        var authorUpdate = new AuthorMemoryUpdate
        {
            StyleLikes = new List<string> { "细腻的心理描写" },
            StyleDislikes = new List<string> { "过于啰嗦的描写", "重复的情绪渲染" }
        };

        Assert.Single(authorUpdate.StyleLikes);
        Assert.Equal(2, authorUpdate.StyleDislikes.Count);
    }

    [Fact]
    public void ExecutionMemoryUpdate_CanSetAllFields()
    {
        var executionUpdate = new ExecutionMemoryUpdate
        {
            ToolSuccess = "WriteChapter 成功，质量门禁通过",
            ToolFailure = null
        };

        Assert.Equal("WriteChapter 成功，质量门禁通过", executionUpdate.ToolSuccess);
        Assert.Null(executionUpdate.ToolFailure);
    }

    [Fact]
    public void AgentMemoryUpdate_IntegrationTest()
    {
        var update = new AgentMemoryUpdate
        {
            SessionMemory = new SessionMemoryUpdate
            {
                ChatSummary = "测试摘要",
                ExtractedPreferences = new List<string> { "偏好1" }
            },
            ProjectMemory = new ProjectMemoryUpdate
            {
                NewConstraints = new List<string> { "约束1" },
                UnresolvedThreads = new List<string> { "伏笔1" }
            },
            AuthorMemory = new AuthorMemoryUpdate
            {
                StyleLikes = new List<string> { "风格喜好1" },
                StyleDislikes = new List<string> { "风格反感1" }
            },
            ExecutionMemory = new ExecutionMemoryUpdate
            {
                ToolSuccess = "成功信息",
                ToolFailure = null
            }
        };

        Assert.Equal("测试摘要", update.SessionMemory.ChatSummary);
        Assert.Single(update.ProjectMemory.NewConstraints);
        Assert.Single(update.AuthorMemory.StyleLikes);
        Assert.Equal("成功信息", update.ExecutionMemory.ToolSuccess);
    }
}

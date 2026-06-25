using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class ConversationKernelTests
{
    [Fact]
    public void Classify_WhenTurnReadsStateThenRequestsPlanOverwrite_LeavesProductionIntentForPlanner()
    {
        var kernel = new ConversationKernel();
        var session = new AgentSession { ActiveProjectId = "project-1" };

        var intent = kernel.Classify(
            session,
            "先查询当前状态和知识库硬事实，然后把第一卷修成10章并覆盖旧规划");

        Assert.Equal(TurnIntentType.FreeChat, intent.Type);
        Assert.Equal("free_chat", intent.Label);
        Assert.Equal("先查询当前状态和知识库硬事实，然后把第一卷修成10章并覆盖旧规划", intent.RawMessage);
    }

    [Fact]
    public void Classify_WhenTurnContinuesChapterProduction_LeavesProductionIntentForPlanner()
    {
        var kernel = new ConversationKernel();
        var session = new AgentSession { ActiveProjectId = "project-1" };

        var intent = kernel.Classify(
            session,
            "继续推进《星渊邮差》第4章。请基于当前工作流真实状态自主判断下一步，目标是通过门禁、质量评审并提交到小说书城；正文不少于3000字。");

        Assert.Equal(TurnIntentType.FreeChat, intent.Type);
        Assert.Equal("free_chat", intent.Label);
    }

    [Fact]
    public void Classify_ExtractsReferencedChineseChapterId()
    {
        var kernel = new ConversationKernel();
        var session = new AgentSession { ActiveProjectId = "project-1" };

        var intent = kernel.Classify(
            session,
            "请修订并重新提交第 6 章《潮信核心》，不要继续第7章。");

        Assert.Equal(TurnIntentType.FreeChat, intent.Type);
        Assert.Equal("chapter-006", intent.ReferencedChapterId);
    }

    [Fact]
    public void Classify_WhenUserAsksShortStatusQuestion_MarksProductionStatusIntent()
    {
        var kernel = new ConversationKernel();
        var session = new AgentSession { ActiveProjectId = "project-1" };

        var intent = kernel.Classify(session, "现在到底执行了吗？");

        Assert.Equal(TurnIntentType.FreeChat, intent.Type);
        Assert.Equal("status_query", intent.Label);
        Assert.Equal("production", intent.SelectionKind);
    }

    [Theory]
    [InlineData("现在是正在写第二章吗？")]
    [InlineData("卡在哪个生产阶段了？")]
    [InlineData("第2章是不是还在执行？")]
    public void Classify_WhenUserAsksProductionProgress_MarksStatusIntent(string message)
    {
        var kernel = new ConversationKernel();
        var session = new AgentSession { ActiveProjectId = "project-1" };

        var intent = kernel.Classify(session, message);

        Assert.Equal("status_query", intent.Label);
        Assert.Equal("production", intent.SelectionKind);
    }
}

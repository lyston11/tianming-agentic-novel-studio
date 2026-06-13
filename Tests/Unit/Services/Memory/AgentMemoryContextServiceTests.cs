using Moq;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class AgentMemoryContextServiceTests
{
    [Fact]
    public async Task BuildAsync_ComposesChatSessionProjectAuthorExecutionAndToolContext()
    {
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetPromptWindowAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatPromptWindowDto(
                "总体摘要",
                Array.Empty<ChatHistorySummaryDto>(),
                new[] { new ChatHistoryTurnDto("user", "刚上传了知识", DateTime.UtcNow) }));

        var repo = new Mock<IAgentMemoryRepository>();
        repo.Setup(x => x.GetSessionMemoryAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionMemory { RecentUploadedKnowledgeIds = new List<string> { "knowledge-1" } });
        repo.Setup(x => x.GetProjectMemoryAsync("user-1", "project-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectMemory
            {
                ImportedKnowledgeIds = new List<string> { "knowledge-1" },
                ReferencedKnowledgeIds = new List<string>()
            });
        repo.Setup(x => x.GetAuthorMemoryAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthorMemory());
        repo.Setup(x => x.GetExecutionMemoryAsync("user-1", "project-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionMemory());

        var service = new AgentMemoryContextService(chat.Object, repo.Object);

        var context = await service.BuildAsync("user-1", "project-1", "session-1", CancellationToken.None);

        Assert.Equal("总体摘要", context.Chat.MetaSummary);
        Assert.Contains("knowledge-1", context.Session.RecentUploadedKnowledgeIds);
        Assert.Contains("knowledge-1", context.Project.ImportedKnowledgeIds);
        Assert.Empty(context.Project.ReferencedKnowledgeIds);
    }
}

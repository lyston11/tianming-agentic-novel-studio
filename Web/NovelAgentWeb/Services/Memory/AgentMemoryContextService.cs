namespace TM.Web.NovelAgentWeb.Services.Memory;

public class AgentMemoryContextService : IAgentMemoryContextService
{
    private readonly IChatHistoryRepository _chatHistory;
    private readonly IAgentMemoryRepository _memoryRepository;

    public AgentMemoryContextService(
        IChatHistoryRepository chatHistory,
        IAgentMemoryRepository memoryRepository)
    {
        _chatHistory = chatHistory;
        _memoryRepository = memoryRepository;
    }

    public async Task<AgentMemoryContextDto> BuildAsync(
        string userId,
        string projectId,
        string sessionId,
        CancellationToken ct = default)
    {
        var chat = await _chatHistory.GetPromptWindowAsync(userId, projectId, sessionId, ct);
        var session = await _memoryRepository.GetSessionMemoryAsync(userId, projectId, sessionId, ct);
        var project = await _memoryRepository.GetProjectMemoryAsync(userId, projectId, ct);
        var author = await _memoryRepository.GetAuthorMemoryAsync(userId, ct);
        var execution = await _memoryRepository.GetExecutionMemoryAsync(userId, projectId, ct);

        return new AgentMemoryContextDto(
            new ChatMemoryContext(chat.MetaSummary, chat.Summaries, chat.RecentMessages),
            session,
            project,
            author,
            execution);
    }
}

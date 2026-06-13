namespace TM.Web.NovelAgentWeb.Services.Memory;

public interface IAgentMemoryEventService
{
    Task AppendAsync(string userId, string? projectId, string? sessionId, string? runId, string sourceType, string triggerType, string memoryScope, string memoryKey, object payload, CancellationToken ct = default);
}

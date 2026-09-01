using Tianming.NovelAgent.Application.Ports;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.AgentApplication;

public sealed class LegacyConversationTextCompletionAdapter(IWritingModelCompletionService completion)
    : IConversationTextCompletionPort
{
    public Task<string> CompleteAsync(
        string userId,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken) =>
        completion.CompleteAsync(userId, systemPrompt, userPrompt, cancellationToken);
}

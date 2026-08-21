using Tianming.NovelAgent.Application.Ports;
using TM.Web.NovelAgentWeb.Services.Context;

namespace TM.Web.NovelAgentWeb.Services.Agent;

public sealed class PiRuntimeContextProvider(
    IConversationContextAssembler contextAssembler,
    IConversationStore conversationStore) : IPiRuntimeContextProvider
{
    public async Task<PiRuntimeContextPayload> BuildAsync(
        string userId,
        string sessionId,
        string? query,
        CancellationToken cancellationToken)
    {
        var context = await contextAssembler.BuildAsync(
            new ConversationContextRequest(userId, sessionId, query),
            cancellationToken);
        var messages = await conversationStore.ReadMessagesAsync(userId, sessionId, cancellationToken);

        return context switch
        {
            UnboundConversationContext unbound => new PiRuntimeContextPayload(
                new PiRuntimeBinding("unbound", null, unbound.BindingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                unbound.SystemInstructions,
                messages,
                null,
                []),
            BoundConversationContext bound => new PiRuntimeContextPayload(
                new PiRuntimeBinding("bound", bound.Binding.ProjectId, bound.BindingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                bound.SystemInstructions,
                messages,
                bound.ProjectSnapshot,
                bound.ProjectTools),
            _ => throw new InvalidOperationException($"Unsupported conversation context: {context.GetType().Name}.")
        };
    }
}

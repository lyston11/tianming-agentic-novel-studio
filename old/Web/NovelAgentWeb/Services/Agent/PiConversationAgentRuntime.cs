using System.Net.Http.Json;
using System.Text.Json;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;

namespace TM.Web.NovelAgentWeb.Services.Agent;

public sealed class PiConversationAgentRuntime(
    HttpClient httpClient,
    IPiRuntimeContextProvider contextProvider) : IConversationAgentRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ConversationRuntimeResult> RunTurnAsync(
        ConversationTurnContext context,
        CancellationToken cancellationToken)
    {
        var runtimeContext = await contextProvider.BuildAsync(
            context.UserId,
            context.SessionId,
            context.Message,
            cancellationToken);
        EnsureBindingConsistency(context.Binding, runtimeContext.Binding);

        var request = new PiRuntimeTurnRequest(
            context.UserId,
            context.SessionId,
            context.Message,
            context.AttachmentIds,
            context.CorrelationId,
            context.SourceUserMessageId,
            runtimeContext.Binding,
            runtimeContext.SystemInstructions,
            runtimeContext.DurableMessages,
            runtimeContext.ProjectContext,
            runtimeContext.AllowedProjectTools);
        using var response = await httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "v1/conversation-turn")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            },
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        PiRuntimeResultResponse? completed = null;
        string? failure = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var runtimeEvent = JsonSerializer.Deserialize<PiRuntimeStreamEvent>(line, JsonOptions)
                ?? throw new InvalidOperationException("Pi Runtime returned an empty stream event.");
            if (runtimeEvent.Type == "completed")
                completed = runtimeEvent.Result;
            else if (runtimeEvent.Type == "failed")
                failure = runtimeEvent.Error;
        }

        if (completed is null)
            throw new InvalidOperationException($"Pi Runtime ended without a completed event. {failure}".Trim());

        return new ConversationRuntimeResult(
            completed.AssistantMessage,
            ConversationDecisionKind.DiscussOnly,
            null,
            completed.TokenDeltas,
            completed.Reason,
            completed.ToolCalls,
            new ConversationRuntimeCheckpoint(
                completed.Checkpoint.Runtime,
                completed.Checkpoint.CheckpointJson),
            completed.Messages);
    }

    private static void EnsureBindingConsistency(
        ConversationBinding binding,
        PiRuntimeBinding runtimeBinding)
    {
        var matches = binding switch
        {
            UnboundConversationBinding => runtimeBinding.State == "unbound" && runtimeBinding.ProjectId is null,
            BoundConversationBinding bound => runtimeBinding.State == "bound"
                && string.Equals(bound.ProjectId, runtimeBinding.ProjectId, StringComparison.Ordinal),
            _ => false
        };
        if (!matches)
            throw new InvalidOperationException("Conversation binding changed while preparing the Pi Runtime request; retry the turn.");
    }
}

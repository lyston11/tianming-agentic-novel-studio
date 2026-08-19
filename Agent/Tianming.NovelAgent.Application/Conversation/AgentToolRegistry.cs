using System.Text.Json;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;

namespace Tianming.NovelAgent.Application.Conversation;

public sealed class AgentToolRegistry(IEnumerable<IAgentTool> tools) : IAgentToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools = tools
        .ToDictionary(tool => tool.Name, StringComparer.Ordinal);

    public Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolCall call,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(call.Name, out var tool))
        {
            return Task.FromResult(new AgentToolExecutionResult(
                call.Name,
                false,
                $"Unknown agent tool '{call.Name}'.",
                Error: $"Unknown agent tool '{call.Name}'."));
        }

        return tool.ExecuteAsync(
            context,
            call.Arguments ?? new Dictionary<string, JsonElement>(),
            cancellationToken);
    }
}

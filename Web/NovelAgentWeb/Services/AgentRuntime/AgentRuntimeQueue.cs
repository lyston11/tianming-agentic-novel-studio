using System.Threading.Channels;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public interface IAgentRuntimeQueue
{
    ValueTask EnqueueAsync(string runtimeRunId, CancellationToken ct = default);
    IAsyncEnumerable<string> DequeueAllAsync(CancellationToken ct = default);
}

public sealed class AgentRuntimeQueue : IAgentRuntimeQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(string runtimeRunId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(runtimeRunId, ct);

    public IAsyncEnumerable<string> DequeueAllAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAllAsync(ct);
}

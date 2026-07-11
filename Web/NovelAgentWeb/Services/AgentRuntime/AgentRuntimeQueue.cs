using System.Threading.Channels;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public interface IAgentRuntimeQueue
{
    ValueTask EnqueueAsync(string runtimeRunId, CancellationToken ct = default);
    IAsyncEnumerable<string> DequeueAllAsync(CancellationToken ct = default);
}

public sealed class AgentRuntimeQueue : IAgentRuntimeQueue
{
    private const int DefaultCapacity = 1000;

    private readonly Channel<string> _channel;

    public AgentRuntimeQueue()
        : this(DefaultCapacity)
    {
    }

    public AgentRuntimeQueue(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Agent runtime queue capacity must be positive.");

        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(string runtimeRunId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(runtimeRunId, ct);

    public IAsyncEnumerable<string> DequeueAllAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAllAsync(ct);
}

using System.Runtime.CompilerServices;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.AgentRuntime;

public class AgentRuntimeWorkerTests
{
    [Fact]
    public async Task RecoverQueuedRunsAsync_EnqueuesPersistedQueuedRunsOnStartup()
    {
        var queuedRuns = new[]
        {
            new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
            {
                Id = "run-queued-1",
                UserId = "user-1",
                SessionId = "session-1",
                Status = AgentRuntimeRunStatus.Queued,
                UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
            {
                Id = "run-queued-2",
                UserId = "user-1",
                SessionId = "session-2",
                Status = AgentRuntimeRunStatus.Queued,
                UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
            }
        };
        var runs = new Mock<IAgentRuntimeRunService>();
        runs.Setup(x => x.ListQueuedAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queuedRuns);
        var queue = new RecordingRuntimeQueue();
        var services = new ServiceCollection()
            .AddSingleton(runs.Object)
            .BuildServiceProvider();
        var worker = new AgentRuntimeWorker(
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeWorker>.Instance,
            new BusyLeaseService());

        var recovered = await worker.RecoverQueuedRunsAsync(CancellationToken.None);

        Assert.Equal(2, recovered);
        Assert.Equal(new[] { "run-queued-1", "run-queued-2" }, queue.EnqueuedRunIds);
    }

    [Fact]
    public async Task AgentRuntimeQueue_EnqueueAsyncWaitsWhenCapacityIsFull()
    {
        var queue = new AgentRuntimeQueue(capacity: 1);
        await queue.EnqueueAsync("run-1");

        var secondEnqueue = queue.EnqueueAsync("run-2").AsTask();
        var completedBeforeRead = await Task.WhenAny(secondEnqueue, Task.Delay(50));
        Assert.NotSame(secondEnqueue, completedBeforeRead);

        await using var enumerator = queue.DequeueAllAsync().GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("run-1", enumerator.Current);

        await secondEnqueue.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExecuteRunWithLeaseAsync_WhenLeaseIsBusyDoesNotResolveRuntimeServices()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var worker = new AgentRuntimeWorker(
            new EmptyRuntimeQueue(),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeWorker>.Instance,
            new BusyLeaseService());

        await worker.ExecuteRunWithLeaseAsync("run-1", CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteRunWithLeaseAsync_WhenLeaseIsAcquiredStartsLeaseRenewal()
    {
        var runs = new Mock<IAgentRuntimeRunService>();
        runs.Setup(x => x.TryGetAsync("run-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun?)null);
        var services = new ServiceCollection()
            .AddSingleton(runs.Object)
            .BuildServiceProvider();
        var locks = new Mock<IDistributedLockService>();
        var distributedLease = new DistributedLockLease(
            Key: "agent_runtime:lock:run-1",
            Token: "token-1",
            Owner: "worker-1",
            AcquiredAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddMinutes(10));
        locks.Setup(x => x.ExtendAsync(
                It.IsAny<DistributedLockLease>(),
                TimeSpan.FromMinutes(10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DistributedLockLease lease, TimeSpan ttl, CancellationToken _) =>
                lease with { ExpiresAt = DateTime.UtcNow.Add(ttl) });
        var lease = new AgentRuntimeRunLease("run-1", distributedLease, locks.Object);
        var worker = new AgentRuntimeWorker(
            new EmptyRuntimeQueue(),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeWorker>.Instance,
            new AcquiredLeaseService(lease));

        await worker.ExecuteRunWithLeaseAsync("run-1", CancellationToken.None);

        locks.Verify(x => x.ExtendAsync(
                It.Is<DistributedLockLease>(l => l.Key == "agent_runtime:lock:run-1"),
                TimeSpan.FromMinutes(10),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task PublishAsync_SendsPersistedRuntimeEventIdToSse()
    {
        await using var db = CreateDb();
        var runtimeEvents = new AgentRuntimeEventService(db);
        var bus = new AgentSseEventBus();
        var reader = bus.GetReader("session-1");
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetUserId()).Returns("user-1");
        var sessions = new AgentSessionManager(db, currentUser.Object, events: bus);
        var run = new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
        {
            Id = "run-1",
            UserId = "user-1",
            SessionId = "session-1",
            ProjectId = "project-1",
            SourceMessageId = "user-message-1"
        };
        var method = typeof(AgentRuntimeWorker).GetMethod("PublishAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(null, new object?[]
        {
            runtimeEvents,
            sessions,
            run,
            AgentSseEventType.RunUpdate,
            "后台执行已完成。",
            new { status = AgentRuntimeRunStatus.Completed },
            CancellationToken.None
        }));
        await task;

        var saved = Assert.Single(db.AgentRuntimeEvents);
        Assert.True(reader.TryRead(out var evt));
        Assert.Equal(saved.Id, evt.EventId);
        Assert.Equal(saved.Type, evt.Type);
        Assert.Equal(saved.RuntimeRunId, evt.RunId);
        Assert.Equal("user-message-1", evt.SourceMessageId);
        Assert.Equal(saved.Message, evt.Message);
        Assert.Equal(saved.DisplaySurface, evt.DisplaySurface);
        Assert.Equal(saved.DisplayPolicy, evt.DisplayPolicy);
        Assert.Contains("user-message-1", saved.DataJson);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private sealed class BusyLeaseService : IAgentRuntimeRunLeaseService
    {
        public Task<AgentRuntimeRunLease?> TryAcquireAsync(string runtimeRunId, CancellationToken ct = default) =>
            Task.FromResult<AgentRuntimeRunLease?>(null);
    }

    private sealed class AcquiredLeaseService : IAgentRuntimeRunLeaseService
    {
        private readonly AgentRuntimeRunLease _lease;

        public AcquiredLeaseService(AgentRuntimeRunLease lease)
        {
            _lease = lease;
        }

        public Task<AgentRuntimeRunLease?> TryAcquireAsync(string runtimeRunId, CancellationToken ct = default) =>
            Task.FromResult<AgentRuntimeRunLease?>(_lease);
    }

    private sealed class EmptyRuntimeQueue : IAgentRuntimeQueue
    {
        public ValueTask EnqueueAsync(string runtimeRunId, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public async IAsyncEnumerable<string> DequeueAllAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class RecordingRuntimeQueue : IAgentRuntimeQueue
    {
        public List<string> EnqueuedRunIds { get; } = new();

        public ValueTask EnqueueAsync(string runtimeRunId, CancellationToken ct = default)
        {
            EnqueuedRunIds.Add(runtimeRunId);
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<string> DequeueAllAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.AgentRuntime;

public class AgentRuntimeEventStreamPumpTests
{
    [Fact]
    public async Task PumpActiveSessionsAsync_DeliversStreamEventsAndAcknowledgesAfterSend()
    {
        var runs = new Mock<IAgentRuntimeRunService>();
        runs.Setup(x => x.ListActiveSessionCursorsAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AgentRuntimeActiveSessionCursor(
                    "run-1",
                    "user-1",
                    "session-1",
                    "project-1",
                    DateTime.UtcNow)
            });
        var consumer = new Mock<IAgentRuntimeEventStreamConsumer>();
        consumer.Setup(x => x.ClaimPendingAsync(
                "session-1",
                "agent-runtime-sse",
                It.IsAny<string>(),
                TimeSpan.FromSeconds(30),
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AgentRuntimeStreamEvent(
                    "1-0",
                    new AgentSseEvent
                    {
                        EventId = "event-pending",
                        SessionId = "session-1",
                        Type = "production_progress",
                        Message = "恢复一条待确认事件"
                    })
            });
        consumer.Setup(x => x.ReadGroupAsync(
                "session-1",
                "agent-runtime-sse",
                It.IsAny<string>(),
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AgentRuntimeStreamEvent(
                    "2-0",
                    new AgentSseEvent
                    {
                        EventId = "event-new",
                        SessionId = "session-1",
                        Type = "production_progress",
                        Message = "读取一条新事件"
                    })
            });
        consumer.Setup(x => x.AcknowledgeAsync(
                "session-1",
                "agent-runtime-sse",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var bus = new AgentSseEventBus();
        var pump = new AgentRuntimeEventStreamPump(
            runs.Object,
            consumer.Object,
            bus,
            new RecordingRuntimeEventService(),
            CreateConfiguration(),
            NullLogger<AgentRuntimeEventStreamPump>.Instance);

        var delivered = await pump.PumpActiveSessionsAsync();

        Assert.Equal(2, delivered);
        var reader = bus.GetReader("session-1");
        Assert.True(reader.TryRead(out var first));
        Assert.True(reader.TryRead(out var second));
        Assert.Equal("event-pending", first.EventId);
        Assert.Equal("event-new", second.EventId);
        consumer.Verify(x => x.EnsureConsumerGroupAsync(
                "session-1",
                "agent-runtime-sse",
                It.IsAny<CancellationToken>()),
            Times.Once);
        consumer.Verify(x => x.AcknowledgeAsync(
                "session-1",
                "agent-runtime-sse",
                "1-0",
                It.IsAny<CancellationToken>()),
            Times.Once);
        consumer.Verify(x => x.AcknowledgeAsync(
                "session-1",
                "agent-runtime-sse",
                "2-0",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PumpActiveSessionsAsync_WhenNoActiveSessionsDoesNotReadRedisStreams()
    {
        var runs = new Mock<IAgentRuntimeRunService>();
        runs.Setup(x => x.ListActiveSessionCursorsAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AgentRuntimeActiveSessionCursor>());
        var consumer = new Mock<IAgentRuntimeEventStreamConsumer>();
        var pump = new AgentRuntimeEventStreamPump(
            runs.Object,
            consumer.Object,
            new AgentSseEventBus(),
            new RecordingRuntimeEventService(),
            CreateConfiguration(),
            NullLogger<AgentRuntimeEventStreamPump>.Instance);

        var delivered = await pump.PumpActiveSessionsAsync();

        Assert.Equal(0, delivered);
        consumer.Verify(x => x.ReadGroupAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PumpActiveSessionsAsync_WhenPendingDeliveryCountExceedsBudgetWritesFailureEventAndAcksPoisonMessage()
    {
        var runs = new Mock<IAgentRuntimeRunService>();
        runs.Setup(x => x.ListActiveSessionCursorsAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AgentRuntimeActiveSessionCursor(
                    "run-1",
                    "user-1",
                    "session-1",
                    "project-1",
                    DateTime.UtcNow)
            });
        var consumer = new Mock<IAgentRuntimeEventStreamConsumer>();
        consumer.Setup(x => x.ClaimPendingAsync(
                "session-1",
                "agent-runtime-sse",
                It.IsAny<string>(),
                TimeSpan.FromSeconds(30),
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AgentRuntimeStreamEvent(
                    "1-0",
                    new AgentSseEvent
                    {
                        EventId = "event-poison",
                        SessionId = "session-1",
                        Type = "production_progress",
                        Message = "反复无法投递的事件"
                    },
                    DeliveryCount: 3,
                    IsPendingRecovery: true)
            });
        consumer.Setup(x => x.ReadGroupAsync(
                "session-1",
                "agent-runtime-sse",
                It.IsAny<string>(),
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AgentRuntimeStreamEvent>());
        consumer.Setup(x => x.AcknowledgeAsync(
                "session-1",
                "agent-runtime-sse",
                "1-0",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var runtimeEvents = new RecordingRuntimeEventService();
        var bus = new AgentSseEventBus();
        var pump = new AgentRuntimeEventStreamPump(
            runs.Object,
            consumer.Object,
            bus,
            runtimeEvents,
            CreateConfiguration(),
            NullLogger<AgentRuntimeEventStreamPump>.Instance);

        var delivered = await pump.PumpActiveSessionsAsync();

        Assert.Equal(0, delivered);
        Assert.False(bus.GetReader("session-1").TryRead(out _));
        var failure = Assert.Single(runtimeEvents.Events);
        Assert.Equal("run-1", failure.RuntimeRunId);
        Assert.Equal("user-1", failure.UserId);
        Assert.Equal("session-1", failure.SessionId);
        Assert.Equal("project-1", failure.ProjectId);
        Assert.Equal("runtime_event_delivery_failed", failure.Type);
        Assert.Equal("runtime_event_stream_pump", failure.Stage);
        Assert.Equal("failed", failure.Status);
        Assert.Equal(AgentRuntimeEventSurface.AdminDebug, failure.DisplaySurface);
        Assert.Equal(AgentRuntimeEventDisplayPolicy.DebugOnly, failure.DisplayPolicy);
        consumer.Verify(x => x.AcknowledgeAsync(
                "session-1",
                "agent-runtime-sse",
                "1-0",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RuntimeEvents:StreamConsumerGroup"] = "agent-runtime-sse",
                ["RuntimeEvents:StreamBatchSize"] = "10",
                ["RuntimeEvents:PendingIdleTime"] = "00:00:30",
                ["RuntimeEvents:MaxPendingDeliveryCount"] = "3"
            })
            .Build();

    private sealed class RecordingRuntimeEventService : IAgentRuntimeEventService
    {
        public List<CreateAgentRuntimeEventRequest> Events { get; } = new();

        public Task<TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent> AppendAsync(
            CreateAgentRuntimeEventRequest request,
            CancellationToken ct = default)
        {
            Events.Add(request);
            return Task.FromResult(new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                RuntimeRunId = request.RuntimeRunId,
                UserId = request.UserId,
                SessionId = request.SessionId,
                ProjectId = request.ProjectId,
                Type = request.Type,
                Stage = request.Stage,
                Status = request.Status,
                Message = request.Message,
                CreatedAt = DateTime.UtcNow
            });
        }

        public Task<IReadOnlyList<TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent>> GetRecentAsync(
            string userId,
            string sessionId,
            int limit = 50,
            CancellationToken ct = default,
            string? afterEventId = null) =>
            Task.FromResult<IReadOnlyList<TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent>>(
                Array.Empty<TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent>());

        public Task<IReadOnlyList<TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent>> GetForRunAsync(
            string runtimeRunId,
            int limit = 100,
            CancellationToken ct = default,
            string? afterEventId = null) =>
            Task.FromResult<IReadOnlyList<TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent>>(
                Array.Empty<TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent>());
    }
}

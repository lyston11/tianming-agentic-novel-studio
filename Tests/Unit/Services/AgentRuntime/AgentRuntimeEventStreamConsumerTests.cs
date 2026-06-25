using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using System.Reflection;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using Xunit;

namespace Tests.Unit.Services.AgentRuntime;

public class AgentRuntimeEventStreamConsumerTests
{
    [Fact]
    public async Task EnsureConsumerGroupAsync_CreatesSessionStreamConsumerGroup()
    {
        var database = new Mock<IDatabase>();
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        var consumer = CreateConsumer(redis.Object);

        await consumer.EnsureConsumerGroupAsync("session-1", "sse-workers");

        database.Verify(x => x.StreamCreateConsumerGroupAsync(
            It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:stream:session-1"),
            "sse-workers",
            "0-0",
            true,
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task ReadGroupAsync_ReadsAndDeserializesPayloadEvents()
    {
        var database = new Mock<IDatabase>();
        database.Setup(x => x.StreamReadGroupAsync(
                It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:stream:session-1"),
                "sse-workers",
                "instance-a",
                ">",
                10,
                CommandFlags.None))
            .ReturnsAsync(new[]
            {
                new StreamEntry("1-0", new[] { new NameValueEntry("payload", Envelope("event-1", "session-1")) })
            });
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        var consumer = CreateConsumer(redis.Object);

        var events = await consumer.ReadGroupAsync("session-1", "sse-workers", "instance-a", 10);

        var evt = Assert.Single(events);
        Assert.Equal("1-0", evt.StreamId);
        Assert.Equal("event-1", evt.Event.EventId);
        Assert.Equal("session-1", evt.Event.SessionId);
    }

    [Fact]
    public async Task AcknowledgeAsync_AcksStreamMessage()
    {
        var database = new Mock<IDatabase>();
        database.Setup(x => x.StreamAcknowledgeAsync(
                It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:stream:session-1"),
                "sse-workers",
                "1-0",
                CommandFlags.None))
            .ReturnsAsync(1);
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        var consumer = CreateConsumer(redis.Object);

        var acknowledged = await consumer.AcknowledgeAsync("session-1", "sse-workers", "1-0");

        Assert.True(acknowledged);
    }

    [Fact]
    public async Task ClaimPendingAsync_ClaimsIdlePendingMessagesAndDeserializesEvents()
    {
        var database = new Mock<IDatabase>();
        database.Setup(x => x.StreamPendingMessagesAsync(
                It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:stream:session-1"),
                "sse-workers",
                10,
                RedisValue.Null,
                null,
                null,
                CommandFlags.None))
            .ReturnsAsync(new[]
            {
                CreatePendingMessage("1-0", "old-instance", 60_000, 1)
            });
        database.Setup(x => x.StreamClaimAsync(
                It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:stream:session-1"),
                "sse-workers",
                "instance-a",
                30_000,
                It.Is<RedisValue[]>(ids => ids.Length == 1 && ids[0] == "1-0"),
                CommandFlags.None))
            .ReturnsAsync(new[]
            {
                new StreamEntry("1-0", new[] { new NameValueEntry("payload", Envelope("event-1", "session-1")) })
            });
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        var consumer = CreateConsumer(redis.Object);

        var events = await consumer.ClaimPendingAsync(
            "session-1",
            "sse-workers",
            "instance-a",
            TimeSpan.FromSeconds(30),
            10);

        var evt = Assert.Single(events);
        Assert.Equal("1-0", evt.StreamId);
        Assert.Equal("event-1", evt.Event.EventId);
    }

    private static IAgentRuntimeEventStreamConsumer CreateConsumer(IConnectionMultiplexer redis)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:InstanceName"] = "Test:"
            })
            .Build();
        return new RedisAgentRuntimeEventStreamConsumer(
            new[] { redis },
            configuration,
            NullLogger<RedisAgentRuntimeEventStreamConsumer>.Instance);
    }

    private static RedisValue Envelope(string eventId, string sessionId)
    {
        return JsonSerializer.Serialize(new
        {
            originId = "instance-a",
            @event = new
            {
                eventId,
                type = "production_progress",
                sessionId,
                message = "运行事件",
                timestamp = "2026-06-23T10:00:00Z"
            }
        });
    }

    private static StreamPendingMessageInfo CreatePendingMessage(
        RedisValue messageId,
        RedisValue consumerName,
        long idleTimeInMilliseconds,
        int deliveryCount)
    {
        return (StreamPendingMessageInfo)Activator.CreateInstance(
            typeof(StreamPendingMessageInfo),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { messageId, consumerName, idleTimeInMilliseconds, deliveryCount },
            culture: null)!;
    }
}

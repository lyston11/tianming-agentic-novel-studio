using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using System.Text.Json;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using Xunit;

namespace Tests.Unit.Services.AgentRuntime;

public class AgentRuntimeEventFanoutTests
{
    [Fact]
    public async Task PublishAsync_WritesEventToDurableSessionListBeforePubSub()
    {
        var database = new Mock<IDatabase>();
        var subscriber = new Mock<ISubscriber>();
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        redis.Setup(x => x.GetSubscriber(It.IsAny<object>())).Returns(subscriber.Object);
        var fanout = CreateFanout(redis.Object);

        await fanout.PublishAsync("session-1", new AgentSseEvent
        {
            EventId = "event-1",
            Type = "production_progress",
            SessionId = "session-1",
            Message = "正在生成正文",
            Timestamp = new DateTime(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc)
        });

        database.Verify(x => x.ListRightPushAsync(
            It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:session-1"),
            It.Is<RedisValue>(value => value.ToString().Contains("\"eventId\":\"event-1\"")),
            When.Always,
            CommandFlags.None), Times.Once);
        database.Verify(x => x.ListTrimAsync(
            It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:session-1"),
            -200,
            -1,
            CommandFlags.None), Times.Once);
        database.Verify(x => x.StreamAddAsync(
            It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:stream:session-1"),
            It.Is<RedisValue>(field => field.ToString() == "payload"),
            It.Is<RedisValue>(value => value.ToString().Contains("\"eventId\":\"event-1\"")),
            null,
            200,
            true,
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task ReplayAsync_ReturnsSessionEventsAfterCursor()
    {
        var values = new RedisValue[]
        {
            Envelope("event-1", "session-1", "第一步"),
            Envelope("event-2", "session-1", "第二步"),
            Envelope("event-3", "session-1", "第三步")
        };
        var database = new Mock<IDatabase>();
        database.Setup(x => x.ListRangeAsync(
                It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:session-1"),
                0,
                -1,
                CommandFlags.None))
            .ReturnsAsync(values);
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        var fanout = CreateFanout(redis.Object);

        var replayed = await fanout.ReplayAsync("session-1", "event-1", 10);

        Assert.Collection(
            replayed,
            evt => Assert.Equal("event-2", evt.EventId),
            evt => Assert.Equal("event-3", evt.EventId));
    }

    [Fact]
    public async Task ReplayAsync_WhenListMissesReadsFromStreamAfterCursor()
    {
        var database = new Mock<IDatabase>();
        database.Setup(x => x.ListRangeAsync(
                It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:session-1"),
                0,
                -1,
                CommandFlags.None))
            .ReturnsAsync(Array.Empty<RedisValue>());
        database.Setup(x => x.StreamRangeAsync(
                It.Is<RedisKey>(key => key.ToString() == "Test:agent_runtime:events:stream:session-1"),
                null,
                null,
                null,
                Order.Ascending,
                CommandFlags.None))
            .ReturnsAsync(new[]
            {
                Stream("1-0", Envelope("event-1", "session-1", "第一步")),
                Stream("2-0", Envelope("event-2", "session-1", "第二步")),
                Stream("3-0", Envelope("event-3", "session-1", "第三步"))
            });
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        var fanout = CreateFanout(redis.Object);

        var replayed = await fanout.ReplayAsync("session-1", "event-1", 10);

        Assert.Collection(
            replayed,
            evt => Assert.Equal("event-2", evt.EventId),
            evt => Assert.Equal("event-3", evt.EventId));
    }

    private static RedisAgentRuntimeEventFanout CreateFanout(IConnectionMultiplexer redis)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:InstanceName"] = "Test:",
                ["RuntimeEvents:ReplayLimit"] = "200",
                ["RuntimeEvents:ReplayTtl"] = "00:10:00"
            })
            .Build();
        return new RedisAgentRuntimeEventFanout(
            new[] { redis },
            configuration,
            NullLogger<RedisAgentRuntimeEventFanout>.Instance);
    }

    private static RedisValue Envelope(string eventId, string sessionId, string message)
    {
        return JsonSerializer.Serialize(new
        {
            originId = "instance-a",
            @event = new
            {
                eventId,
                type = "production_progress",
                sessionId,
                message,
                timestamp = "2026-06-23T10:00:00Z"
            }
        });
    }

    private static StreamEntry Stream(string id, RedisValue payload) =>
        new(id, new[] { new NameValueEntry("payload", payload) });
}

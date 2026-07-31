using TM.Web.NovelAgentWeb.Support;
using TM.Web.NovelAgentWeb.DTOs;
using Xunit;

namespace Tests.Unit.Support;

public sealed class AgentSseEventBusTests
{
    [Fact]
    public async Task SendAsync_BroadcastsToEveryActiveSubscriber()
    {
        var bus = new AgentSseEventBus();
        await using var first = bus.Subscribe("user-1", "session-1");
        await using var second = bus.Subscribe("user-1", "session-1");

        await bus.SendAsync("user-1", "session-1", new AgentSseEvent
        {
            EventId = "event-1",
            Type = AgentSseEventType.RunUpdate,
            Message = "running"
        });

        Assert.True(first.Reader.TryRead(out var firstEvent));
        Assert.True(second.Reader.TryRead(out var secondEvent));
        Assert.Equal("event-1", firstEvent.EventId);
        Assert.Equal("event-1", secondEvent.EventId);
    }

    [Fact]
    public async Task Dispose_RemovesOnlyThatSubscriber()
    {
        var bus = new AgentSseEventBus();
        var first = bus.Subscribe("user-1", "session-1");
        await using var second = bus.Subscribe("user-1", "session-1");
        await first.DisposeAsync();

        await bus.SendAsync("user-1", "session-1", new AgentSseEvent
        {
            EventId = "event-2",
            Type = AgentSseEventType.RunUpdate,
            Message = "completed"
        });

        Assert.False(first.Reader.TryRead(out _));
        Assert.True(second.Reader.TryRead(out var secondEvent));
        Assert.Equal("event-2", secondEvent.EventId);
    }

    [Fact]
    public async Task Subscribe_WithoutBacklog_DropsEventsThatAuthoritativeReplayWillSupply()
    {
        var bus = new AgentSseEventBus();
        await bus.SendAsync("user-1", "session-1", new AgentSseEvent
        {
            EventId = "event-before-replay",
            Type = AgentSseEventType.RunUpdate
        });

        await using var subscription = bus.Subscribe("user-1", "session-1", includeBacklog: false);

        Assert.False(subscription.Reader.TryRead(out _));

        await bus.SendAsync("user-1", "session-1", new AgentSseEvent
        {
            EventId = "event-during-replay",
            Type = AgentSseEventType.RunUpdate
        });
        Assert.True(subscription.Reader.TryRead(out var liveEvent));
        Assert.Equal("event-during-replay", liveEvent.EventId);
    }

    [Fact]
    public async Task SameSessionId_IsIsolatedByAuthenticatedUserScope()
    {
        var bus = new AgentSseEventBus();
        await using var firstUser = bus.Subscribe("user-1", "shared-session");
        await using var secondUser = bus.Subscribe("user-2", "shared-session");

        await bus.SendAsync("user-1", "shared-session", new AgentSseEvent
        {
            Type = AgentSseEventType.RunUpdate,
            Message = "user-1-only"
        });

        Assert.True(firstUser.Reader.TryRead(out var delivered));
        Assert.Equal("user-1-only", delivered.Message);
        Assert.False(secondUser.Reader.TryRead(out _));
    }

    [Fact]
    public async Task SendAsync_AssignsOneStableEventIdBeforeBroadcast()
    {
        var bus = new AgentSseEventBus();
        await using var first = bus.Subscribe("user-1", "session-1");
        await using var second = bus.Subscribe("user-1", "session-1");
        var evt = new AgentSseEvent { Type = AgentSseEventType.RunUpdate };

        await bus.SendAsync("user-1", "session-1", evt);

        Assert.True(first.Reader.TryRead(out var firstEvent));
        Assert.True(second.Reader.TryRead(out var secondEvent));
        Assert.False(string.IsNullOrWhiteSpace(firstEvent.EventId));
        Assert.Equal(firstEvent.EventId, secondEvent.EventId);
        Assert.Equal(firstEvent.EventId, evt.EventId);
    }
}

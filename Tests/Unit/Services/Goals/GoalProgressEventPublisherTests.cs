using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.Goals;

public sealed class GoalProgressEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_UsesOwnedSourceSessionAndDoesNotWriteLegacyRuntimeEvents()
    {
        await using var db = CreateDb();
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = "goal-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SourceSessionId = "session-1",
            HumanReadableObjective = "写第一章",
            Status = "running",
            TotalCostLimit = 10,
            IdempotencyKey = "goal-1"
        });
        db.KernelTasks.Add(new KernelTask
        {
            Id = "task-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskGraphVersionId = "graph-1",
            KernelName = "tianming_writing",
            TaskType = "WriteCandidate",
            Status = "completed",
            Attempt = 1,
            IdempotencyKey = "task-1"
        });
        await db.SaveChangesAsync();
        var local = new AgentSseEventBus();
        var fanout = new RecordingFanout();
        var publisher = new GoalProgressEventPublisher(
            db,
            local,
            fanout,
            NullLogger<GoalProgressEventPublisher>.Instance);
        await using var subscription = local.Subscribe("user-1", "session-1", includeBacklog: false);

        await publisher.PublishAsync(new GoalProgressEventRequest(
            "user-1",
            "goal-1",
            AgentSseEventType.GoalTaskCompleted,
            "候选正文已完成。",
            TaskId: "task-1"));

        var evt = await subscription.Reader.ReadAsync();
        var data = Assert.IsType<GoalProgressEventData>(evt.Data);
        Assert.Equal(AgentSseEventType.GoalTaskCompleted, evt.Type);
        Assert.Equal("task-1", evt.StepId);
        Assert.Equal("goal-1", data.GoalId);
        Assert.Equal("session-1", evt.SessionId);
        Assert.Equal(("user-1", "session-1"), fanout.Scope);
        Assert.Empty(await db.AgentRuntimeEvents.ToListAsync());
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private sealed class RecordingFanout : IAgentRuntimeEventFanout
    {
        public (string UserId, string SessionId)? Scope { get; private set; }

        public Task PublishAsync(
            string userId,
            string sessionId,
            AgentSseEvent evt,
            CancellationToken ct = default)
        {
            Scope = (userId, sessionId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentSseEvent>> ReplayAsync(
            string userId,
            string sessionId,
            string? afterEventId = null,
            int limit = 100,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentSseEvent>>([]);
    }
}

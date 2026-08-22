using Microsoft.EntityFrameworkCore;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Execution;
using Xunit;

using Tests.Unit.Support;

namespace Tests.Unit.Services.Execution;

public sealed class ModelExecutionRecoveryServiceTests
{
    [Fact]
    public async Task OutcomeUnknown_ChargesReservationAndStoresProviderResultAsUnadopted()
    {
        await using var db = CreateDb();
        Seed(db, attempt: 1);
        await db.SaveChangesAsync();
        var provider = new Mock<IModelExecutionOutcomeResolver>();
        provider.Setup(item => item.QueryAsync(It.IsAny<ModelExecution>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderExecutionOutcome(
                ProviderExecutionOutcomeStatus.Found,
                "{\"text\":\"迟到正文\"}",
                "late-hash"));
        var service = new ModelExecutionRecoveryService(db, provider.Object, new GoalBudgetService(db), new Tests.Unit.Support.LegacyControlPlaneCommandTestDouble(db));

        var result = await service.RecoverAsync("user-1", "execution-1");

        Assert.Equal(ModelExecutionRecoveryAction.StoredUnadoptedResult, result.Action);
        var execution = await db.ModelExecutions.SingleAsync();
        Assert.Equal("recovered_unadopted", execution.Status);
        Assert.Equal(0.4m, execution.ActualCost);
        var goal = await db.CreativeGoals.SingleAsync();
        Assert.Equal(0m, goal.ReservedCost);
        Assert.Equal(0.4m, goal.ActualCost);
        Assert.Equal("unadopted", (await db.KernelArtifacts.SingleAsync()).Status);
        Assert.Empty(await db.DomainEvents.ToListAsync());
    }

    [Fact]
    public async Task UnknownOutcome_RetriesAtMostOnceAndLateOldResultNeverAdopts()
    {
        await using var db = CreateDb();
        Seed(db, attempt: 1);
        await db.SaveChangesAsync();
        var provider = new Mock<IModelExecutionOutcomeResolver>();
        provider.Setup(item => item.QueryAsync(It.IsAny<ModelExecution>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderExecutionOutcome(ProviderExecutionOutcomeStatus.NotFound, null, null));
        var service = new ModelExecutionRecoveryService(db, provider.Object, new GoalBudgetService(db), new Tests.Unit.Support.LegacyControlPlaneCommandTestDouble(db));

        var first = await service.RecoverAsync("user-1", "execution-1");
        Assert.Equal(ModelExecutionRecoveryAction.RetryScheduled, first.Action);
        Assert.Equal("ready", (await db.KernelTasks.SingleAsync()).Status);
        await service.RecordLateResultAsync("user-1", "execution-1", "{\"text\":\"旧请求迟到\"}", "old-late-hash");
        db.ModelExecutions.Add(new ModelExecution
        {
            Id = "execution-2",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskId = "task-1",
            KernelName = "tianming_writing",
            Provider = "provider",
            Model = "model",
            Status = "running",
            ReservedCost = 0.3m,
            Attempt = 2
        });
        var goal = await db.CreativeGoals.SingleAsync();
        goal.ReservedCost = 0.3m;
        await db.SaveChangesAsync();
        var second = await service.RecoverAsync("user-1", "execution-2");

        Assert.Contains(await db.KernelArtifacts.ToListAsync(), artifact =>
            artifact.Status == "unadopted" && artifact.ContentHash == "old-late-hash");
        Assert.Equal(ModelExecutionRecoveryAction.GoalTerminated, second.Action);
        Assert.Equal("failed", (await db.CreativeGoals.SingleAsync()).Status);
        Assert.Equal("failed", (await db.KernelTasks.SingleAsync()).Status);
    }

    private static void Seed(NovelAgentDbContext db, int attempt)
    {
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = "goal-1",
            UserId = "user-1",
            ProjectId = "project-1",
            TotalCostLimit = 1m,
            ReservedCost = 0.4m,
            Status = "running",
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
            TaskType = "WriteChapter",
            Status = "running",
            MaxAttempts = 2,
            IdempotencyKey = "task-1"
        });
        db.ModelExecutions.Add(new ModelExecution
        {
            Id = "execution-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskId = "task-1",
            KernelName = "tianming_writing",
            Provider = "provider",
            Model = "model",
            Status = "running",
            ReservedCost = 0.4m,
            Attempt = attempt
        });
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}

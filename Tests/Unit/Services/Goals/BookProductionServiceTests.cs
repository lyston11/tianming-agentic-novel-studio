using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Goals;

public sealed class BookProductionServiceTests
{
    [Fact]
    public async Task InitializeAsync_CreatesFirstBatchFromWholeBookContract()
    {
        await using var db = CreateDb();
        var goal = Goal("interactive_batch");
        db.CreativeGoals.Add(goal);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var production = await service.InitializeAsync(goal);

        Assert.Equal((1, 12, 5, 1), (
            production.TargetStartChapterNumber,
            production.TargetEndChapterNumber,
            production.BatchSize,
            production.CurrentBatchNumber));
        var batch = Assert.Single(await db.ProductionBatches.ToListAsync());
        Assert.Equal((1, 5, "user"), (batch.StartChapterNumber, batch.EndChapterNumber, batch.AcceptanceActor));
    }

    [Fact]
    public async Task CompleteBatchAsync_InteractiveStrategyStopsAtNextBatchBoundary()
    {
        await using var db = CreateDb();
        var goal = Goal("interactive_batch");
        db.CreativeGoals.Add(goal);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var production = await service.InitializeAsync(goal);
        var batch = await service.GetCurrentBatchAsync(goal.Id);
        batch.CanonBranchId = "branch-1";
        await db.SaveChangesAsync();

        var result = await service.CompleteBatchAsync(goal.Id, "branch-1");

        Assert.False(result.ShouldCompileNextBatch);
        Assert.Equal("awaiting_user", result.Production.Status);
        Assert.Equal((2, 6, 10), (
            result.NextBatch!.BatchNumber,
            result.NextBatch.StartChapterNumber,
            result.NextBatch.EndChapterNumber));
        Assert.Equal("awaiting_next_batch", (await db.CreativeGoals.SingleAsync()).Status);
    }

    [Fact]
    public async Task CompleteBatchAsync_FullAutoStrategyRequestsNextCompilation()
    {
        await using var db = CreateDb();
        var goal = Goal("full_auto");
        db.CreativeGoals.Add(goal);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var production = await service.InitializeAsync(goal);
        var batch = await service.GetCurrentBatchAsync(goal.Id);
        batch.CanonBranchId = "branch-1";
        await db.SaveChangesAsync();

        var result = await service.CompleteBatchAsync(goal.Id, "branch-1");

        Assert.True(result.ShouldCompileNextBatch);
        Assert.Equal("running", result.Production.Status);
        Assert.Equal("agent-policy", result.NextBatch!.AcceptanceActor);
    }

    private static CreativeGoal Goal(string strategy) => new()
    {
        Id = "goal-1",
        UserId = "user-1",
        ProjectId = "project-1",
        SourceSessionId = "session-1",
        GoalType = "write_book",
        CollaborationMode = "coauthor",
        HumanReadableObjective = "完成一本小说",
        TargetChapterRangeJson = "{\"start\":1,\"end\":12}",
        ExecutionStrategy = strategy,
        BookPlanJson = "{\"batchSize\":5}",
        TotalCostLimit = 100,
        IdempotencyKey = "goal-1"
    };

    private static BookProductionService CreateService(NovelAgentDbContext db) =>
        new(db, new StubCurrentUserService(), new PassingBookValidationService());

    private sealed class PassingBookValidationService : IBookValidationService
    {
        public Task<BookValidationReport> ValidateAsync(BookValidationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BookValidationReport { OverallStatus = "validated" });
    }

    private static NovelAgentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public string GetUserId() => "user-1";
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => "user-1";
    }
}

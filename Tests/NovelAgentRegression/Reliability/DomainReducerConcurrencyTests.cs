using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.Goals;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Reliability;

public sealed class DomainReducerConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDb();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE ROLE novelagent_app LOGIN PASSWORD 'novelagent_app'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            CREATE ROLE novelagent_worker LOGIN PASSWORD 'novelagent_worker'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            GRANT CONNECT ON DATABASE postgres TO novelagent_app;
            GRANT CONNECT ON DATABASE postgres TO novelagent_worker;
            GRANT USAGE ON SCHEMA public TO novelagent_app;
            GRANT USAGE ON SCHEMA public TO novelagent_worker;
            """);
        await db.Database.MigrateAsync();
        await SeedRunningTaskAsync(db);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ApplyAsync_ConcurrentReducersAssignDistinctSequentialAggregateVersions()
    {
        var claim = CreateClaim();
        await using var dbA = CreateDb();
        await using var dbB = CreateDb();
        var reducerA = CreateReducer(dbA);
        var reducerB = CreateReducer(dbB);
        var eventA = CreateEvent("event-concurrent-a");
        var eventB = CreateEvent("event-concurrent-b");

        await Task.WhenAll(
            reducerA.ApplyAsync(claim, [], [eventA]),
            reducerB.ApplyAsync(claim, [], [eventB]));

        await using var verify = CreateDb();
        Assert.Equal(
            new long[] { 1, 2 },
            await verify.DomainEvents
                .Where(item =>
                    item.UserId == claim.UserId &&
                    item.AggregateType == "candidate_chapter" &&
                    item.AggregateId == "candidate-concurrent")
                .OrderBy(item => item.AggregateVersion)
                .Select(item => item.AggregateVersion)
                .ToArrayAsync());
    }

    private DomainReducer CreateReducer(NovelAgentDbContext db)
    {
        var currentUser = new StubCurrentUserService("user-domain-concurrency");
        return new DomainReducer(
            db,
            currentUser,
            new DomainContractValidator(db, currentUser),
            new KernelArtifactStore(db));
    }

    private PostgresNovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }

    private static DomainEventProposal CreateEvent(string idempotencyKey) => new(
        "candidate_chapter",
        "candidate-concurrent",
        1,
        "ChapterPlanProduced",
        [],
        [],
        "{}",
        idempotencyKey);

    private static KernelTaskClaim CreateClaim() => new(
        "task-domain-concurrency",
        "user-domain-concurrency",
        "project-domain-concurrency",
        "goal-domain-concurrency",
        "graph-domain-concurrency",
        null,
        "narrative_planning",
        "PlanChapter",
        1,
        "worker-domain-concurrency",
        DateTime.UtcNow.AddMinutes(5));

    private static async Task SeedRunningTaskAsync(NovelAgentDbContext db)
    {
        var now = DateTime.UtcNow;
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = "goal-domain-concurrency",
            UserId = "user-domain-concurrency",
            ProjectId = "project-domain-concurrency",
            HumanReadableObjective = "验证聚合版本原子分配",
            TotalCostLimit = 10m,
            Status = "running",
            IdempotencyKey = "goal-domain-concurrency",
            CreatedAt = now
        });
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = "graph-domain-concurrency",
            UserId = "user-domain-concurrency",
            ProjectId = "project-domain-concurrency",
            GoalId = "goal-domain-concurrency",
            Version = 1,
            Status = "active",
            GraphJson = "{}",
            ContentHash = "graph-domain-concurrency",
            CreatedAt = now
        });
        db.KernelTasks.Add(new KernelTask
        {
            Id = "task-domain-concurrency",
            UserId = "user-domain-concurrency",
            ProjectId = "project-domain-concurrency",
            GoalId = "goal-domain-concurrency",
            TaskGraphVersionId = "graph-domain-concurrency",
            KernelName = "narrative_planning",
            TaskType = "PlanChapter",
            Status = "running",
            LeaseOwner = "worker-domain-concurrency",
            LeaseExpiresAt = now.AddMinutes(10),
            Attempt = 1,
            MaxAttempts = 2,
            IdempotencyKey = "task-domain-concurrency",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private sealed class StubCurrentUserService(string userId) : ICurrentUserService
    {
        public string GetUserId() => userId;
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => userId;
    }
}

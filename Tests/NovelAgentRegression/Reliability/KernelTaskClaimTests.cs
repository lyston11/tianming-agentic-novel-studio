using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Goals;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Reliability;

public sealed class KernelTaskClaimTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _applicationConnectionString = string.Empty;
    private string _workerConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var admin = CreateAdminDbContext();
        await admin.Database.ExecuteSqlRawAsync("""
            CREATE ROLE novelagent_app LOGIN PASSWORD 'novelagent_app'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            CREATE ROLE novelagent_worker LOGIN PASSWORD 'novelagent_worker'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            GRANT CONNECT ON DATABASE postgres TO novelagent_app;
            GRANT CONNECT ON DATABASE postgres TO novelagent_worker;
            GRANT USAGE ON SCHEMA public TO novelagent_app;
            GRANT USAGE ON SCHEMA public TO novelagent_worker;
            """);
        await admin.Database.MigrateAsync();
        await admin.Database.ExecuteSqlRawAsync("""
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO novelagent_app;
            GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO novelagent_app;
            """);

        _applicationConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = "novelagent_app",
            Password = "novelagent_app"
        }.ConnectionString;
        _workerConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = "novelagent_worker",
            Password = "novelagent_worker"
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ConcurrentWorkers_AtomicallyClaimDifferentTasks()
    {
        await SeedTasksAsync(
            CreateTask("task-1", "user-1", priority: 0),
            CreateTask("task-2", "user-2", priority: 1));
        await using var dbA = CreateApplicationDbContext();
        await using var dbB = CreateApplicationDbContext();
        var schedulerA = CreateScheduler(dbA);
        var schedulerB = CreateScheduler(dbB);

        var claims = await Task.WhenAll(
            schedulerA.ClaimNextAsync("worker-a", TimeSpan.FromMinutes(2)),
            schedulerB.ClaimNextAsync("worker-b", TimeSpan.FromMinutes(2)));

        Assert.All(claims, claim => Assert.NotNull(claim));
        Assert.Equal(2, claims.Select(claim => claim!.TaskId).Distinct().Count());
        Assert.Equal(["user-1", "user-2"], claims.Select(claim => claim!.UserId).Order().ToArray());
    }

    [Fact]
    public async Task ClaimNextAsync_RecoversExpiredRunningLease()
    {
        var expired = CreateTask("expired-task", "user-1", priority: 0);
        expired.Status = "running";
        expired.LeaseOwner = "dead-worker";
        expired.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        await SeedTasksAsync(expired);
        await using var db = CreateApplicationDbContext();
        var scheduler = CreateScheduler(db);

        var claim = await scheduler.ClaimNextAsync("recovery-worker", TimeSpan.FromMinutes(2));

        Assert.NotNull(claim);
        Assert.Equal("expired-task", claim.TaskId);
        Assert.Equal("recovery-worker", claim.LeaseOwner);
        Assert.Equal(1, claim.Attempt);
    }

    [Fact]
    public async Task RenewAsync_ExtendsOnlyTheOwnedRunningLease()
    {
        var task = CreateTask("renewed-task", "user-1", priority: 0);
        await SeedTasksAsync(task);
        await using var db = CreateApplicationDbContext();
        var scheduler = CreateScheduler(db);
        var claim = await scheduler.ClaimNextAsync("worker-heartbeat", TimeSpan.FromSeconds(30));
        Assert.NotNull(claim);
        await SetUserScopeAsync(db, claim!.UserId);

        var renewed = await scheduler.RenewAsync(claim, TimeSpan.FromMinutes(10));

        Assert.True(renewed);
        await using var admin = CreateAdminDbContext();
        var persisted = await admin.KernelTasks.SingleAsync(item => item.Id == task.Id);
        Assert.Equal("worker-heartbeat", persisted.LeaseOwner);
        Assert.True(persisted.LeaseExpiresAt > claim.LeaseExpiresAt.AddMinutes(5));
    }

    [Fact]
    public async Task ClaimNextAsync_DoesNotDispatchTasksForPausedGoal()
    {
        var task = CreateTask("paused-goal-task", "user-1", priority: 0);
        await SeedTasksAsync(task);
        await using (var admin = CreateAdminDbContext())
        {
            var goal = await admin.CreativeGoals.SingleAsync(item => item.Id == task.GoalId);
            goal.Status = "pause_requested";
            await admin.SaveChangesAsync();
        }
        await using var db = CreateApplicationDbContext();
        var scheduler = CreateScheduler(db);

        var claim = await scheduler.ClaimNextAsync("worker-paused", TimeSpan.FromMinutes(2));

        Assert.Null(claim);
    }

    [Fact]
    public async Task ClaimNextAsync_DoesNotDispatchReadyTaskFromSupersededGraph()
    {
        var task = CreateTask("superseded-ready-task", "user-1", priority: 0);
        await SeedTasksAsync(task);
        await using (var admin = CreateAdminDbContext())
        {
            var graph = await admin.TaskGraphVersions.SingleAsync(item => item.Id == task.TaskGraphVersionId);
            graph.Status = "superseded";
            await admin.SaveChangesAsync();
        }
        await using var db = CreateApplicationDbContext();
        var scheduler = CreateScheduler(db);

        var claim = await scheduler.ClaimNextAsync("worker-superseded", TimeSpan.FromMinutes(2));

        Assert.Null(claim);
    }

    [Fact]
    public async Task ClaimNextAsync_DoesNotDispatchHumanAcceptanceOrPrefixMergeTasks()
    {
        var dependency = CreateTask("graph-1:batch-impact", "user-1", 0);
        var acceptance = CreateTask("graph-1:acceptance-gate", "user-1", 1, "AcceptanceGate");
        acceptance.Status = "blocked";
        acceptance.DependencyTaskIdsJson = "[\"batch-impact\"]";
        var prefixMerge = CreateTask("graph-1:prefix-merge", "user-1", 2, "PrefixMerge");
        prefixMerge.Status = "blocked";
        prefixMerge.DependencyTaskIdsJson = "[\"acceptance-gate\"]";
        await SeedTasksAsync(dependency, acceptance, prefixMerge);
        await using var db = CreateApplicationDbContext();
        var scheduler = CreateScheduler(db);
        var dependencyClaim = await scheduler.ClaimNextAsync("worker-manual-gate", TimeSpan.FromMinutes(2));
        Assert.NotNull(dependencyClaim);
        await SetUserScopeAsync(db, dependencyClaim!.UserId);
        await scheduler.CompleteAsync(dependencyClaim, ["impact-artifact"]);

        var claim = await scheduler.ClaimNextAsync("worker-manual-gate", TimeSpan.FromMinutes(2));

        Assert.Null(claim);
        await using var admin = CreateAdminDbContext();
        Assert.Equal("awaiting_user", (await admin.KernelTasks.SingleAsync(task => task.Id == acceptance.Id)).Status);
        Assert.Equal("blocked", (await admin.KernelTasks.SingleAsync(task => task.Id == prefixMerge.Id)).Status);
    }

    [Fact]
    public async Task CompleteAsync_UnblocksOnlyTasksWhoseDependenciesAreSatisfied()
    {
        var dependency = CreateTask("graph-1:dependency", "user-1", 0);
        var dependent = CreateTask("graph-1:dependent", "user-1", 1);
        dependent.Status = "blocked";
        dependent.DependencyTaskIdsJson = "[\"dependency\"]";
        await SeedTasksAsync(dependency, dependent);
        await using var db = CreateApplicationDbContext();
        var scheduler = CreateScheduler(db);
        var claim = await scheduler.ClaimNextAsync("worker-a", TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await SetUserScopeAsync(db, claim!.UserId);

        await scheduler.CompleteAsync(claim, ["artifact-1"]);

        await using var admin = CreateAdminDbContext();
        var completed = await admin.KernelTasks.AsNoTracking().SingleAsync(task => task.Id == dependency.Id);
        var unblocked = await admin.KernelTasks.AsNoTracking().SingleAsync(task => task.Id == dependent.Id);
        Assert.Equal("completed", completed.Status);
        Assert.Equal("[\"artifact-1\"]", completed.OutputArtifactIdsJson);
        Assert.Equal("ready", unblocked.Status);
    }

    [Fact]
    public async Task FailAsync_RetriesWithBackoffThenMovesGoalToAwaitingDecision()
    {
        var task = CreateTask("retry-write", "user-1", 0, "WriteCandidate");
        task.MaxAttempts = KernelTaskFailurePolicy.MaxAttempts(task.TaskType);
        await SeedTasksAsync(task);
        await using var db = CreateApplicationDbContext();
        var scheduler = CreateScheduler(db);

        var first = await scheduler.ClaimNextAsync("worker-retry", TimeSpan.FromMinutes(2));
        Assert.NotNull(first);
        await SetUserScopeAsync(db, first!.UserId);
        await scheduler.FailAsync(first, new KernelTaskFailure(
            KernelTaskFailureCategory.Transient,
            "provider timeout"));

        await using (var admin = CreateAdminDbContext())
        {
            var retrying = await admin.KernelTasks.SingleAsync(item => item.Id == task.Id);
            Assert.Equal("ready", retrying.Status);
            Assert.True(retrying.NextAttemptAt > DateTime.UtcNow);
            Assert.Equal("running", (await admin.CreativeGoals.SingleAsync()).Status);
            retrying.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await admin.SaveChangesAsync();
        }

        var second = await scheduler.ClaimNextAsync("worker-retry", TimeSpan.FromMinutes(2));
        Assert.NotNull(second);
        await scheduler.FailAsync(second!, new KernelTaskFailure(
            KernelTaskFailureCategory.Transient,
            "provider timeout again"));

        await using var verify = CreateAdminDbContext();
        var exhausted = await verify.KernelTasks.SingleAsync(item => item.Id == task.Id);
        Assert.Equal("awaiting_decision", exhausted.Status);
        Assert.Equal("transient", exhausted.FailureKind);
        Assert.Equal("awaiting_decision", (await verify.CreativeGoals.SingleAsync()).Status);
    }

    private async Task SeedTasksAsync(params KernelTask[] tasks)
    {
        await using var admin = CreateAdminDbContext();
        admin.CreativeGoals.AddRange(tasks
            .GroupBy(task => (task.UserId, task.GoalId))
            .Select(group => new CreativeGoal
            {
                Id = group.Key.GoalId,
                UserId = group.Key.UserId,
                ProjectId = group.First().ProjectId,
                HumanReadableObjective = "reliability test",
                TotalCostLimit = 10m,
                Status = "running",
                IdempotencyKey = group.Key.GoalId
            }));
        admin.TaskGraphVersions.AddRange(tasks
            .GroupBy(task => (task.UserId, task.GoalId, task.TaskGraphVersionId))
            .Select(group => new TaskGraphVersion
            {
                Id = group.Key.TaskGraphVersionId,
                UserId = group.Key.UserId,
                ProjectId = group.First().ProjectId,
                GoalId = group.Key.GoalId,
                Version = 1,
                Status = "active",
                GraphJson = "{}",
                ContentHash = group.Key.TaskGraphVersionId,
                CreatedAt = DateTime.UtcNow
            }));
        admin.KernelTasks.AddRange(tasks);
        await admin.SaveChangesAsync();
    }

    private static KernelTask CreateTask(
        string id,
        string userId,
        int priority,
        string taskType = "TestTask") => new()
    {
        Id = id,
        UserId = userId,
        ProjectId = "project-1",
        GoalId = $"goal-{userId}",
        TaskGraphVersionId = $"graph-{userId}",
        KernelName = "test_kernel",
        TaskType = taskType,
        Status = "ready",
        IdempotencyKey = id,
        Priority = priority,
        MaxAttempts = 2,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private PostgresNovelAgentDbContext CreateAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }

    private PostgresNovelAgentDbContext CreateApplicationDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_applicationConnectionString)
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }

    private PostgresKernelTaskScheduler CreateScheduler(NovelAgentDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovelAgentWorkerDb"] = _workerConnectionString
            })
            .Build();
        return new PostgresKernelTaskScheduler(
            db,
            new BackgroundClaimConnectionFactory(configuration),
            new ClaimOnlyTransitionService(db));
    }

    private sealed class ClaimOnlyTransitionService : IBookProductionTransitionService
    {
        private readonly NovelAgentDbContext _db;

        public ClaimOnlyTransitionService(NovelAgentDbContext db)
        {
            _db = db;
        }

        private static InvalidOperationException Unsupported() => new("Claim test does not execute production transitions.");
        public Task<AdvanceAfterAcceptanceResult> AdvanceAfterAcceptanceAsync(string goalId, string branchId, string actor, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<TaskGraphDefinition> ContinueInteractiveAsync(string goalId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task BlockAsync(string goalId, string batchId, CancellationToken cancellationToken = default) => throw Unsupported();

        public async Task ApplyTaskFailureAsync(
            KernelTaskClaim claim,
            KernelTaskFailureDisposition disposition,
            CancellationToken cancellationToken = default)
        {
            var goal = await _db.CreativeGoals.SingleAsync(item =>
                item.Id == claim.GoalId && item.UserId == claim.UserId,
                cancellationToken);
            goal.Status = disposition == KernelTaskFailureDisposition.AwaitingDecision
                ? "awaiting_decision"
                : "failed";
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SetUserScopeAsync(PostgresNovelAgentDbContext db, string userId)
    {
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_user_id', {userId}, false)");
    }
}

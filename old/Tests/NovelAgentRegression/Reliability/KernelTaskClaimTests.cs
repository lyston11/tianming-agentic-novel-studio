using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;
using Tianming.NovelAgent.Infrastructure;
using Tianming.NovelAgent.Application.Production;
using Tianming.NovelAgent.Infrastructure.Persistence;
using TM.Web.NovelAgentWeb.Services.AgentApplication;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Goals;
using Xunit;
using AgentProductionMode = Tianming.NovelAgent.Domain.Goals.ProductionMode;

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
        await using (var agentMigration = CreateAgentAdminDbContext())
            await agentMigration.Database.MigrateAsync();
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
        var userScopeA = new AgentUserScope();
        var userScopeB = new AgentUserScope();
        await using var dbA = CreateApplicationDbContext(userScopeA);
        await using var dbB = CreateApplicationDbContext(userScopeB);
        var schedulerA = CreateScheduler(dbA, userScopeA);
        var schedulerB = CreateScheduler(dbB, userScopeB);

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
        var userScope = new AgentUserScope();
        await using var db = CreateApplicationDbContext(userScope);
        var scheduler = CreateScheduler(db, userScope);

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
        var userScope = new AgentUserScope();
        await using var db = CreateApplicationDbContext(userScope);
        var scheduler = CreateScheduler(db, userScope);
        var claim = await scheduler.ClaimNextAsync("worker-heartbeat", TimeSpan.FromSeconds(30));
        Assert.NotNull(claim);

        var renewed = await scheduler.RenewAsync(claim!, TimeSpan.FromMinutes(10));

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
        var userScope = new AgentUserScope();
        await using var db = CreateApplicationDbContext(userScope);
        var scheduler = CreateScheduler(db, userScope);

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
        var userScope = new AgentUserScope();
        await using var db = CreateApplicationDbContext(userScope);
        var scheduler = CreateScheduler(db, userScope);

        var claim = await scheduler.ClaimNextAsync("worker-superseded", TimeSpan.FromMinutes(2));

        Assert.Null(claim);
    }

    [Fact]
    public async Task ClaimNextAsync_DoesNotDispatchHumanAcceptanceOrPrefixMergeTasks()
    {
        var dependency = CreateTask("graph-1:batch-impact", "user-1", 0);
        var acceptance = CreateTask("graph-1:acceptance-gate", "user-1", 1, "AcceptanceGate");
        acceptance.Status = "blocked";
        acceptance.BranchId = "branch-1";
        acceptance.DependencyTaskIdsJson = "[\"batch-impact\"]";
        var prefixMerge = CreateTask("graph-1:prefix-merge", "user-1", 2, "PrefixMerge");
        prefixMerge.Status = "blocked";
        prefixMerge.DependencyTaskIdsJson = "[\"acceptance-gate\"]";
        await SeedTasksAsync(dependency, acceptance, prefixMerge);
        await using (var setupDb = CreateAdminDbContext())
        {
            var graph = await setupDb.TaskGraphVersions.SingleAsync(item => item.Id == acceptance.TaskGraphVersionId);
            graph.GoalRevisionId = "revision-1";
            graph.GraphJson = JsonSerializer.Serialize(
                Tianming.NovelAgent.Domain.Production.FirstBatchTaskGraphCompiler.Compile(
                    graph.Id,
                    AgentProductionMode.InteractiveBatch,
                    1),
                new JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });
            setupDb.BookProductions.Add(new BookProduction
            {
                Id = "production-1",
                UserId = acceptance.UserId,
                ProjectId = acceptance.ProjectId,
                GoalId = acceptance.GoalId,
                Status = "running",
                TargetStartChapterNumber = 1,
                TargetEndChapterNumber = 1,
                NextChapterNumber = 1
            });
            setupDb.ProductionBatches.Add(new ProductionBatch
            {
                Id = "batch-1",
                UserId = acceptance.UserId,
                ProjectId = acceptance.ProjectId,
                GoalId = acceptance.GoalId,
                BookProductionId = "production-1",
                BatchNumber = 1,
                StartChapterNumber = 1,
                EndChapterNumber = 1,
                Status = "running",
                TaskGraphVersionId = acceptance.TaskGraphVersionId,
                CanonBranchId = acceptance.BranchId
            });
            await setupDb.SaveChangesAsync();
            await setupDb.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE book_productions
                SET goal_revision_id = {"revision-1"},
                    task_graph_version_id = {acceptance.TaskGraphVersionId}
                WHERE id = {"production-1"}
                """);
        }
        var userScope = new AgentUserScope();
        await using var db = CreateApplicationDbContext(userScope);
        var scheduler = CreateScheduler(db, userScope);
        var dependencyClaim = await scheduler.ClaimNextAsync("worker-manual-gate", TimeSpan.FromMinutes(2));
        Assert.NotNull(dependencyClaim);
        await scheduler.CompleteAsync(dependencyClaim!, ["impact-artifact"]);

        var claim = await scheduler.ClaimNextAsync("worker-manual-gate", TimeSpan.FromMinutes(2));

        Assert.Null(claim);
        await using var verifyDb = CreateAdminDbContext();
        Assert.Equal("awaiting_user", (await verifyDb.KernelTasks.SingleAsync(task => task.Id == acceptance.Id)).Status);
        Assert.Equal("blocked", (await verifyDb.KernelTasks.SingleAsync(task => task.Id == prefixMerge.Id)).Status);
        var bridge = await verifyDb.OutboxEvents.SingleAsync(item =>
            item.EventType == "novel_agent_acceptance_gate_reached");
        Assert.Equal("production-1", bridge.AggregateId);
        Assert.Equal($"novel-agent:acceptance-gate:{acceptance.Id}", bridge.IdempotencyKey);

        var agentOptions = new DbContextOptionsBuilder<AgentControlDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var agentDb = new AgentControlDbContext(agentOptions);
        var ids = new TestIdGenerator();
        var clock = new TestClock();
        var store = new EfAgentControlStore(agentDb, clock, ids, new Sha256ContractHasher());
        var production = new ProductionApplicationService(
            store,
            store,
            new OutboxCanonMergePort(agentDb, ids, clock),
            store,
            store,
            ids,
            clock);
        var handler = new NovelAgentOutboxHandler(
            verifyDb,
            new Moq.Mock<IPrefixMergeService>(Moq.MockBehavior.Strict).Object,
            production,
            new AgentUserScope());
        await handler.HandleAsync(bridge, CancellationToken.None);
        await handler.HandleAsync(bridge, CancellationToken.None);

        agentDb.ChangeTracker.Clear();
        Assert.Equal("awaiting_acceptance", await agentDb.BookProductions
            .Where(item => item.Id == "production-1")
            .Select(item => item.Status)
            .SingleAsync());
        Assert.Single(await agentDb.DomainEvents
            .Where(item => item.EventType == "ProductionAwaitingAcceptance")
            .ToListAsync());
    }

    [Fact]
    public async Task CompleteAsync_UnblocksOnlyTasksWhoseDependenciesAreSatisfied()
    {
        var dependency = CreateTask("graph-1:dependency", "user-1", 0);
        var dependent = CreateTask("graph-1:dependent", "user-1", 1);
        dependent.Status = "blocked";
        dependent.DependencyTaskIdsJson = "[\"dependency\"]";
        await SeedTasksAsync(dependency, dependent);
        var userScope = new AgentUserScope();
        await using var db = CreateApplicationDbContext(userScope);
        var scheduler = CreateScheduler(db, userScope);
        var claim = await scheduler.ClaimNextAsync("worker-a", TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);

        await scheduler.CompleteAsync(claim!, ["artifact-1"]);

        await using var admin = CreateAdminDbContext();
        var completed = await admin.KernelTasks.AsNoTracking().SingleAsync(task => task.Id == dependency.Id);
        var unblocked = await admin.KernelTasks.AsNoTracking().SingleAsync(task => task.Id == dependent.Id);
        Assert.Equal("completed", completed.Status);
        Assert.Equal("[\"artifact-1\"]", completed.OutputArtifactIdsJson);
        Assert.Equal("ready", unblocked.Status);
    }

    [Fact]
    public async Task CompleteAsync_LostLeaseRollsBackWithoutUnblockingDependents()
    {
        var dependency = CreateTask("graph-1:lease-lost", "user-1", 0);
        var dependent = CreateTask("graph-1:after-lease-lost", "user-1", 1);
        dependent.Status = "blocked";
        dependent.DependencyTaskIdsJson = "[\"lease-lost\"]";
        await SeedTasksAsync(dependency, dependent);
        var userScope = new AgentUserScope();
        await using var db = CreateApplicationDbContext(userScope);
        var scheduler = CreateScheduler(db, userScope);
        var claim = await scheduler.ClaimNextAsync("worker-original", TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);

        await using (var admin = CreateAdminDbContext())
        {
            var claimedTask = await admin.KernelTasks.SingleAsync(item => item.Id == dependency.Id);
            claimedTask.LeaseOwner = "worker-replacement";
            await admin.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scheduler.CompleteAsync(claim!, ["must-not-persist"]));

        await using var verify = CreateAdminDbContext();
        var unchanged = await verify.KernelTasks.AsNoTracking().SingleAsync(item => item.Id == dependency.Id);
        var stillBlocked = await verify.KernelTasks.AsNoTracking().SingleAsync(item => item.Id == dependent.Id);
        Assert.Equal("running", unchanged.Status);
        Assert.Equal("[]", unchanged.OutputArtifactIdsJson);
        Assert.Equal("blocked", stillBlocked.Status);
    }

    [Fact]
    public async Task FailAsync_RetriesWithBackoffThenMovesGoalToAwaitingDecision()
    {
        var task = CreateTask("retry-write", "user-1", 0, "WriteCandidate");
        task.MaxAttempts = KernelTaskFailurePolicy.MaxAttempts(task.TaskType);
        await SeedTasksAsync(task);
        var userScope = new AgentUserScope();
        await using var db = CreateApplicationDbContext(userScope);
        var scheduler = CreateScheduler(db, userScope);

        var first = await scheduler.ClaimNextAsync("worker-retry", TimeSpan.FromMinutes(2));
        Assert.NotNull(first);
        await scheduler.FailAsync(first!, new KernelTaskFailure(
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

    private AgentControlDbContext CreateApplicationDbContext(AgentUserScope userScope)
    {
        var options = new DbContextOptionsBuilder<AgentControlDbContext>()
            .UseNpgsql(_applicationConnectionString)
            .AddInterceptors(new AgentUserScopeConnectionInterceptor(userScope))
            .Options;
        return new AgentControlDbContext(options);
    }

    private AgentControlDbContext CreateAgentAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<AgentControlDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new AgentControlDbContext(options);
    }

    private PostgresKernelTaskScheduler CreateScheduler(
        AgentControlDbContext db,
        AgentUserScope userScope)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovelAgentWorkerDb"] = _workerConnectionString
            })
            .Build();
        return new PostgresKernelTaskScheduler(
            db,
            userScope,
            new BackgroundClaimConnectionFactory(configuration));
    }

    private sealed class TestIdGenerator : Tianming.NovelAgent.Application.Ports.IIdGenerator
    {
        private int _next;
        public string NewId() => $"bridge-test-{Interlocked.Increment(ref _next)}";
    }

    private sealed class TestClock : Tianming.NovelAgent.Application.Ports.IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}

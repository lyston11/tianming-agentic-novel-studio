using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Embedding;
using TM.Web.NovelAgentWeb.Services.Execution;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Reliability;

public sealed class GoalBudgetConcurrencyTests : IAsyncLifetime
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
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = "goal-budget",
            UserId = "user-1",
            ProjectId = "project-1",
            HumanReadableObjective = "并发预算测试",
            TotalCostLimit = 1m,
            Status = "running",
            IdempotencyKey = "goal-budget"
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task GoalEmbeddingExecution_PersistsCompletedAuditWithoutChangingMonetaryBudget()
    {
        await using var db = CreateDb();
        var scopes = new KernelModelExecutionScopeAccessor();
        using var _ = scopes.Push(new KernelModelExecutionScope(
            "user-1", "project-1", "goal-budget", "task-embedding", "knowledge_retrieval", 1));
        var envelope = new GoalEmbeddingExecutionEnvelope(
            new GoalBudgetService(db),
            scopes,
            new EmbeddingRuntimeStatus
            {
                Provider = "bge-small-zh",
                Model = "bge-small-zh-v1.5",
                Dimension = 3
            });

        var vector = await envelope.ExecuteAsync(
            "北塔钟声与归家承诺",
            EmbeddingMode.Query,
            _ => Task.FromResult(new[] { 0.1f, 0.2f, 0.3f }));

        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, vector);
        db.ChangeTracker.Clear();
        var execution = await db.ModelExecutions.AsNoTracking().SingleAsync(item => item.TaskId == "task-embedding");
        var goal = await db.CreativeGoals.AsNoTracking().SingleAsync(item => item.Id == "goal-budget");
        Assert.Equal("embedding", execution.KernelName);
        Assert.Equal("completed", execution.Status);
        Assert.Equal(0, execution.ReservedCost);
        Assert.Equal(0, execution.ActualCost);
        Assert.Equal(0, goal.ReservedCost);
        Assert.Equal(0, goal.ActualCost);
    }

    [Fact]
    public async Task ConcurrentWorstCaseReservations_NeverExceedGoalLimit()
    {
        var attempts = Enumerable.Range(1, 10).Select(async attempt =>
        {
            await using var db = CreateDb();
            var service = new GoalBudgetService(db);
            return await service.ReserveAsync(new GoalBudgetReservationRequest(
                "user-1",
                "project-1",
                "goal-budget",
                $"task-{attempt}",
                "tianming_writing",
                "config-v1",
                "provider",
                "model",
                0.3m,
                1));
        });

        var results = await Task.WhenAll(attempts);

        Assert.Equal(3, results.Count(result => result.Reserved));
        await using var verify = CreateDb();
        var goal = await verify.CreativeGoals.AsNoTracking().SingleAsync();
        Assert.Equal(0.9m, goal.ReservedCost);
        Assert.True(goal.ReservedCost + goal.ActualCost <= goal.TotalCostLimit);
        Assert.Equal(3, await verify.ModelExecutions.CountAsync());
    }

    [Fact]
    public async Task ConcurrentDuplicateReservation_UsesOneExecutionAndOneBudgetHold()
    {
        var attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = CreateDb();
            return await new GoalBudgetService(db).ReserveAsync(new GoalBudgetReservationRequest(
                "user-1", "project-1", "goal-budget", "task-duplicate", "setting",
                "config-v1", "provider", "model", 0.3m, 1));
        });

        var results = await Task.WhenAll(attempts);

        Assert.All(results, result => Assert.True(result.Reserved));
        Assert.Single(results.Select(result => result.ExecutionId).Distinct());
        await using var verify = CreateDb();
        var goal = await verify.CreativeGoals.AsNoTracking().SingleAsync();
        Assert.Equal(0.3m, goal.ReservedCost);
        Assert.Equal(1, await verify.ModelExecutions.CountAsync());
    }

    [Fact]
    public async Task SettleAsync_ReleasesReservationAndChargesActualCostAtomically()
    {
        await using var db = CreateDb();
        var service = new GoalBudgetService(db);
        var reservation = await service.ReserveAsync(new GoalBudgetReservationRequest(
            "user-1", "project-1", "goal-budget", "task-settle", "literary_review",
            "config-v1", "provider", "model", 0.5m, 1));
        Assert.True(reservation.Reserved);

        await service.MarkProviderCallStartedAsync(
            "user-1",
            reservation.ExecutionId!,
            "owner-settle",
            TimeSpan.FromSeconds(600));
        await service.RecordResultAsync(new ModelExecutionResultRecord(
            "user-1",
            reservation.ExecutionId!,
            "{\"result\":\"ok\"}",
            0.18m,
            1200,
            600,
            "request-1",
            "result-hash",
            ProviderLeaseOwner: "owner-settle"));
        await service.SettleAsync("user-1", reservation.ExecutionId!);

        db.ChangeTracker.Clear();
        var goal = await db.CreativeGoals.AsNoTracking().SingleAsync();
        var execution = await db.ModelExecutions.AsNoTracking().SingleAsync(item => item.Id == reservation.ExecutionId);
        Assert.Equal(0m, goal.ReservedCost);
        Assert.Equal(0.18m, goal.ActualCost);
        Assert.Equal("completed", execution.Status);
        Assert.Equal(0.18m, execution.ActualCost);
    }

    [Fact]
    public async Task FailKnownAsync_ReleasesReservationAndClosesExecutionAtomically()
    {
        await using var db = CreateDb();
        var service = new GoalBudgetService(db);
        var reservation = await service.ReserveAsync(new GoalBudgetReservationRequest(
            "user-1", "project-1", "goal-budget", "task-known-failure", "narrative_planning",
            "config-v1", "provider", "model", 0.5m, 1));
        await service.MarkProviderCallStartedAsync(
            "user-1",
            reservation.ExecutionId!,
            "owner-known-failure",
            TimeSpan.FromSeconds(60));

        await service.FailKnownAsync(
            "user-1",
            reservation.ExecutionId!,
            "owner-known-failure",
            "provider rejected request");

        db.ChangeTracker.Clear();
        var goal = await db.CreativeGoals.AsNoTracking().SingleAsync();
        var execution = await db.ModelExecutions.AsNoTracking()
            .SingleAsync(item => item.Id == reservation.ExecutionId);
        Assert.Equal(0m, goal.ReservedCost);
        Assert.Equal(0m, goal.ActualCost);
        Assert.Equal("failed", execution.Status);
        Assert.Equal("provider rejected request", execution.ErrorMessage);
        Assert.Null(execution.LeaseExpiresAt);
        Assert.NotNull(execution.CompletedAt);
    }

    [Fact]
    public async Task LatestRevisionCostLimit_IsUsedWithoutMutatingCommittedGoal()
    {
        const string goalId = "goal-revision-budget";
        await using (var seed = CreateDb())
        {
            seed.CreativeGoals.Add(new CreativeGoal
            {
                Id = goalId,
                UserId = "user-1",
                ProjectId = "project-1",
                HumanReadableObjective = "Revision 预算测试",
                TotalCostLimit = 0.1m,
                Status = "running",
                IdempotencyKey = goalId
            });
            seed.GoalRevisions.Add(new GoalRevision
            {
                Id = "revision-budget-1",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = goalId,
                RevisionNumber = 1,
                Reason = "用户追加预算",
                ConstraintChangesJson = "{\"totalCostLimit\":0.5}"
            });
            seed.GoalRevisions.Add(new GoalRevision
            {
                Id = "revision-budget-2",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = goalId,
                RevisionNumber = 2,
                Reason = "只调整写作目标，不修改预算",
                ConstraintChangesJson = "{\"humanReadableObjective\":\"保持节奏紧凑\",\"totalCostLimit\":null}"
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = CreateDb())
        {
            var result = await new GoalBudgetService(db).ReserveAsync(new GoalBudgetReservationRequest(
                "user-1", "project-1", goalId, "task-revision-budget", "setting",
                "config-v1", "provider", "model", 0.3m, 1));
            Assert.True(result.Reserved);
            Assert.Equal(0.2m, result.RemainingBudget);
            await new GoalBudgetService(db).ChargeOutcomeUnknownAsync(
                "user-1",
                result.ExecutionId!);
        }

        await using var verify = CreateDb();
        var committedGoal = await verify.CreativeGoals.AsNoTracking().SingleAsync(item => item.Id == goalId);
        Assert.Equal(0.1m, committedGoal.TotalCostLimit);
        Assert.Equal(0m, committedGoal.ReservedCost);
        Assert.Equal(0.3m, committedGoal.ActualCost);
        Assert.Equal("running", committedGoal.Status);
    }

    [Fact]
    public async Task ConcurrentSettlement_ChargesOnePersistedResultExactlyOnce()
    {
        const string goalId = "goal-idempotent-settlement";
        string executionId;
        await using (var seed = CreateDb())
        {
            seed.CreativeGoals.Add(new CreativeGoal
            {
                Id = goalId,
                UserId = "user-1",
                ProjectId = "project-1",
                HumanReadableObjective = "幂等结算测试",
                TotalCostLimit = 1m,
                Status = "running",
                IdempotencyKey = goalId
            });
            await seed.SaveChangesAsync();
            var service = new GoalBudgetService(seed);
            var reservation = await service.ReserveAsync(new GoalBudgetReservationRequest(
                "user-1", "project-1", goalId, "task-idempotent-settlement", "setting",
                "config-v1", "provider", "model", 0.5m, 1));
            executionId = reservation.ExecutionId!;
            await service.MarkProviderCallStartedAsync(
                "user-1",
                executionId,
                "owner-idempotent-settlement",
                TimeSpan.FromSeconds(600));
            await service.RecordResultAsync(new ModelExecutionResultRecord(
                "user-1", executionId, "{\"result\":\"ok\"}",
                0.18m, 1200, 600, "request-idempotent", "result-hash-idempotent",
                ProviderLeaseOwner: "owner-idempotent-settlement"));
        }

        var settlements = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = CreateDb();
            await new GoalBudgetService(db).SettleAsync("user-1", executionId);
        });
        await Task.WhenAll(settlements);

        await using var verify = CreateDb();
        var goal = await verify.CreativeGoals.AsNoTracking().SingleAsync(item => item.Id == goalId);
        var execution = await verify.ModelExecutions.AsNoTracking().SingleAsync(item => item.Id == executionId);
        Assert.Equal(0m, goal.ReservedCost);
        Assert.Equal(0.18m, goal.ActualCost);
        Assert.Equal("completed", execution.Status);
    }

    [Fact]
    public async Task RunningExecution_IsClaimedOnlyAfterItsProviderLeaseExpires()
    {
        await using var db = CreateDb();
        var service = new GoalBudgetService(db);
        var reservation = await service.ReserveAsync(new GoalBudgetReservationRequest(
            "user-1", "project-1", "goal-budget", "task-provider-lease", "setting",
            "config-v1", "provider", "model", 0.3m, 1));
        await service.MarkProviderCallStartedAsync(
            "user-1",
            reservation.ExecutionId!,
            "owner-provider-lease",
            TimeSpan.FromSeconds(600));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE model_executions
            SET created_at = clock_timestamp() - interval '10 minutes'
            WHERE id = {reservation.ExecutionId!}
            """);

        var claimedBeforeExpiry = await ClaimStaleExecutionAsync(120);

        Assert.Null(claimedBeforeExpiry);
        db.ChangeTracker.Clear();
        var active = await db.ModelExecutions.AsNoTracking()
            .SingleAsync(item => item.Id == reservation.ExecutionId);
        Assert.Equal("running", active.Status);
        Assert.NotNull(active.StartedAt);
        Assert.True(active.LeaseExpiresAt > DateTime.UtcNow.AddMinutes(9));

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE model_executions
            SET lease_expires_at = clock_timestamp() - interval '1 second'
            WHERE id = {reservation.ExecutionId!}
            """);

        var claimedAfterExpiry = await ClaimStaleExecutionAsync(120);

        Assert.Equal(reservation.ExecutionId, claimedAfterExpiry);
    }

    [Fact]
    public async Task ResultReceived_IsSettledAtomicallyByBackgroundWorker()
    {
        await using var db = CreateDb();
        var service = new GoalBudgetService(db);
        var reservation = await service.ReserveAsync(new GoalBudgetReservationRequest(
            "user-1", "project-1", "goal-budget", "task-background-settle", "setting",
            "config-v1", "provider", "model", 0.3m, 1));
        await service.MarkProviderCallStartedAsync(
            "user-1",
            reservation.ExecutionId!,
            "owner-background-settle",
            TimeSpan.FromSeconds(60));
        await service.RecordResultAsync(new ModelExecutionResultRecord(
            "user-1", reservation.ExecutionId!, "{\"result\":\"ok\"}",
            0.12m, 100, 50, "request-background-settle", "hash-background-settle",
            ProviderLeaseOwner: "owner-background-settle"));

        var settledExecutionId = await SettleNextExecutionAsWorkerAsync();

        Assert.Equal(reservation.ExecutionId, settledExecutionId);
        db.ChangeTracker.Clear();
        var execution = await db.ModelExecutions.AsNoTracking()
            .SingleAsync(item => item.Id == reservation.ExecutionId);
        var goal = await db.CreativeGoals.AsNoTracking().SingleAsync();
        Assert.Equal("completed", execution.Status);
        Assert.Equal(0m, goal.ReservedCost);
        Assert.Equal(0.12m, goal.ActualCost);
    }

    [Fact]
    public async Task ConcurrentProviderLeaseAcquisition_AllowsOnlyOneOwner()
    {
        string executionId;
        await using (var seed = CreateDb())
        {
            var reservation = await new GoalBudgetService(seed).ReserveAsync(new GoalBudgetReservationRequest(
                "user-1", "project-1", "goal-budget", "task-exclusive-provider-lease", "setting",
                "config-v1", "provider", "model", 0.3m, 1));
            executionId = reservation.ExecutionId!;
        }

        var acquisitions = new[] { "owner-a", "owner-b" }.Select(async owner =>
        {
            try
            {
                await using var db = CreateDb();
                await new GoalBudgetService(db).MarkProviderCallStartedAsync(
                    "user-1", executionId, owner, TimeSpan.FromSeconds(60));
                return owner;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        });

        var owners = await Task.WhenAll(acquisitions);

        Assert.Single(owners.Where(owner => owner != null));
        await using var verify = CreateDb();
        var execution = await verify.ModelExecutions.AsNoTracking().SingleAsync(item => item.Id == executionId);
        Assert.Equal(owners.Single(owner => owner != null), execution.ProviderLeaseOwner);
    }

    [Fact]
    public async Task BrokenSettlementRecord_IsQuarantinedWithoutBlockingLaterResults()
    {
        await using var db = CreateDb();
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = "goal-broken-settlement",
            UserId = "user-1",
            ProjectId = "project-1",
            HumanReadableObjective = "损坏结算记录",
            TotalCostLimit = 1m,
            ReservedCost = 0,
            Status = "running",
            IdempotencyKey = "goal-broken-settlement"
        });
        db.ModelExecutions.Add(new ModelExecution
        {
            Id = "execution-broken-settlement",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-broken-settlement",
            TaskId = "task-broken-settlement",
            KernelName = "setting",
            ModelConfigVersionId = "config-v1",
            Provider = "provider",
            Model = "model",
            IdempotencyKey = "execution-broken-settlement",
            Status = "result_received",
            ReservedCost = 0.3m,
            ActualCost = 0.1m,
            ResultJson = "{\"result\":\"broken\"}",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();
        var service = new GoalBudgetService(db);
        var valid = await service.ReserveAsync(new GoalBudgetReservationRequest(
            "user-1", "project-1", "goal-budget", "task-valid-after-broken", "setting",
            "config-v1", "provider", "model", 0.3m, 1));
        await service.MarkProviderCallStartedAsync(
            "user-1", valid.ExecutionId!, "owner-valid-after-broken", TimeSpan.FromSeconds(60));
        await service.RecordResultAsync(new ModelExecutionResultRecord(
            "user-1", valid.ExecutionId!, "{\"result\":\"valid\"}", 0.1m,
            100, 20, "request-valid-after-broken", "hash-valid-after-broken",
            ProviderLeaseOwner: "owner-valid-after-broken"));

        Assert.Equal("execution-broken-settlement", await SettleNextExecutionAsWorkerAsync());
        Assert.Equal(valid.ExecutionId, await SettleNextExecutionAsWorkerAsync());

        db.ChangeTracker.Clear();
        var broken = await db.ModelExecutions.AsNoTracking()
            .SingleAsync(item => item.Id == "execution-broken-settlement");
        var completed = await db.ModelExecutions.AsNoTracking()
            .SingleAsync(item => item.Id == valid.ExecutionId);
        Assert.Equal("settlement_failed", broken.Status);
        Assert.NotNull(broken.ErrorMessage);
        Assert.Equal("completed", completed.Status);
    }

    private async Task<string?> ClaimStaleExecutionAsync(int staleSeconds)
    {
        await using var db = CreateDb();
        return await db.Database.SqlQueryRaw<string>(
                "SELECT execution_id AS \"Value\" FROM claim_stale_model_execution({0})",
                staleSeconds)
            .SingleOrDefaultAsync();
    }

    private async Task<string?> SettleNextExecutionAsWorkerAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = "novelagent_worker",
            Password = "novelagent_worker"
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT execution_id FROM settle_next_model_execution()",
            connection);
        return (string?)await command.ExecuteScalarAsync();
    }

    private PostgresNovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }
}

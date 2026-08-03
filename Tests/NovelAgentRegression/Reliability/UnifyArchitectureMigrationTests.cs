using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Reliability;

public sealed class UnifyArchitectureMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260802055744_PersistAgentKnowledgeContext";
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
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task LatestMigration_BackfillsNeutralGateActorAndCatalog_WithoutDroppingHistoricalAuditTable()
    {
        await using (var db = CreateDb())
        {
            db.Users.Add(new User
            {
                Id = "migration-user",
                Username = "migration-user",
                Email = "migration-user@example.test",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProject
            {
                Id = "migration-project",
                UserId = "migration-user",
                Title = "migration project"
            });
            db.KnowledgeBases.Add(new KnowledgeBase
            {
                Id = "migration-knowledge",
                UserId = "migration-user",
                SourceProjectId = "migration-project",
                EntryType = "HardFact",
                Title = "迁移事实",
                Content = "迁移正文"
            });
            db.CreativeGoals.Add(new CreativeGoal
            {
                Id = "migration-goal",
                UserId = "migration-user",
                ProjectId = "migration-project",
                HumanReadableObjective = "验证迁移",
                TotalCostLimit = 10,
                Status = "running",
                IdempotencyKey = "migration-goal"
            });
            db.TaskGraphVersions.Add(new TaskGraphVersion
            {
                Id = "migration-graph",
                UserId = "migration-user",
                ProjectId = "migration-project",
                GoalId = "migration-goal",
                Version = 1,
                Status = "active",
                GraphJson = "{\"nodes\":[{\"taskType\":\"UserAcceptance\"}]}",
                ContentHash = "migration-graph"
            });
            db.KernelTasks.Add(new KernelTask
            {
                Id = "migration-graph:user-acceptance",
                UserId = "migration-user",
                ProjectId = "migration-project",
                GoalId = "migration-goal",
                TaskGraphVersionId = "migration-graph",
                KernelName = "workflow",
                TaskType = "UserAcceptance",
                Status = "awaiting_user",
                IdempotencyKey = "migration-acceptance"
            });
            db.BookProductions.Add(new BookProduction
            {
                Id = "migration-production",
                UserId = "migration-user",
                ProjectId = "migration-project",
                GoalId = "migration-goal",
                TargetStartChapterNumber = 1,
                TargetEndChapterNumber = 1,
                NextChapterNumber = 1,
                BatchSize = 1,
                CurrentBatchNumber = 1
            });
            db.ProductionBatches.Add(new ProductionBatch
            {
                Id = "migration-batch",
                UserId = "migration-user",
                ProjectId = "migration-project",
                GoalId = "migration-goal",
                BookProductionId = "migration-production",
                BatchNumber = 1,
                StartChapterNumber = 1,
                EndChapterNumber = 1,
                Status = "running",
                AcceptanceActor = "agent",
                TaskGraphVersionId = "migration-graph"
            });
            await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
        }

        await using var verify = CreateDb();
        Assert.Equal("AcceptanceGate", (await verify.KernelTasks.SingleAsync()).TaskType);
        Assert.Contains("AcceptanceGate", (await verify.TaskGraphVersions.SingleAsync()).GraphJson);
        Assert.Equal("agent-policy", (await verify.ProductionBatches.SingleAsync()).AcceptanceActor);
        var catalog = await verify.KnowledgeCatalogStates.SingleAsync(item => item.UserId == "migration-user");
        Assert.Equal(1, catalog.Revision);
        Assert.Equal(1, catalog.ActiveEntryCount);
        Assert.Equal(
            "agent_tool_search_snapshots",
            await verify.Database.SqlQueryRaw<string>("SELECT to_regclass('public.agent_tool_search_snapshots')::text AS \"Value\"")
                .SingleAsync());
        var leaseColumns = await verify.Database.SqlQueryRaw<string>("""
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'agent_chat_request_receipts'
                  AND column_name IN ('lease_owner', 'lease_expires_at')
                ORDER BY column_name
                """)
            .ToArrayAsync();
        Assert.Equal(["lease_expires_at", "lease_owner"], leaseColumns);
    }

    [Fact]
    public async Task CatalogProjection_ConcurrentMutationsIncrementRevisionAndCountWithoutLostUpdates()
    {
        await using (var seed = CreateDb())
        {
            await seed.Database.MigrateAsync();
            seed.Users.Add(new User
            {
                Id = "catalog-user",
                Username = "catalog-user",
                Email = "catalog-user@example.test",
                PasswordHash = "hash",
                Role = "author"
            });
            seed.NovelProjects.Add(new NovelProject
            {
                Id = "catalog-project",
                UserId = "catalog-user",
                Title = "catalog project"
            });
            await seed.SaveChangesAsync();
            await KnowledgeCatalogProjection.GetOrRebuildAsync(seed, "catalog-user", CancellationToken.None);
        }

        async Task MutateAsync(string knowledgeId)
        {
            await using var db = CreateDb();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.KnowledgeBases.Add(new KnowledgeBase
            {
                Id = knowledgeId,
                UserId = "catalog-user",
                SourceProjectId = "catalog-project",
                EntryType = "HardFact",
                Title = knowledgeId,
                Content = knowledgeId
            });
            await db.SaveChangesAsync();
            await KnowledgeCatalogProjection.TouchAtomicAsync(db, "catalog-user", 1, CancellationToken.None);
            await transaction.CommitAsync();
        }

        await Task.WhenAll(MutateAsync("knowledge-a"), MutateAsync("knowledge-b"));

        await using var verify = CreateDb();
        var state = await verify.KnowledgeCatalogStates.SingleAsync(item => item.UserId == "catalog-user");
        Assert.Equal(3, state.Revision);
        Assert.Equal(2, state.ActiveEntryCount);
    }

    private PostgresNovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Interceptors;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Models.Auth;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Security;

public sealed class PostgresTenantIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();
    private string _applicationConnectionString = string.Empty;
    private string _workerConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var adminDb = CreateAdminDbContext();
        await adminDb.Database.ExecuteSqlRawAsync("""
            CREATE ROLE novelagent_app LOGIN PASSWORD 'novelagent_app'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            CREATE ROLE novelagent_worker LOGIN PASSWORD 'novelagent_worker'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            GRANT CONNECT ON DATABASE postgres TO novelagent_app;
            GRANT CONNECT ON DATABASE postgres TO novelagent_worker;
            GRANT USAGE ON SCHEMA public TO novelagent_app;
            GRANT USAGE ON SCHEMA public TO novelagent_worker;
            """);
        await adminDb.Database.MigrateAsync();
        await adminDb.Database.ExecuteSqlRawAsync("""
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
    public async Task CreativeGoals_AreInvisibleAcrossDatabaseUserScopes()
    {
        await using (var userADb = CreateDbContext())
        {
            await SetUserScopeAsync(userADb, "user-a");
            userADb.CreativeGoals.Add(new CreativeGoal
            {
                Id = "goal-a",
                UserId = "user-a",
                ProjectId = "shared-local-id",
                SourceSessionId = "session-a",
                GoalType = "write_batch",
                HumanReadableObjective = "写三章候选正文",
                TotalCostLimit = 10m,
                CanonBaselineVersion = "canon-1",
                KnowledgeSnapshotVersion = "knowledge-1",
                QualityContractVersion = "quality-1",
                StyleProfileVersion = "style-1",
                IdempotencyKey = "goal-a"
            });
            await userADb.SaveChangesAsync();
        }

        await using var userBDb = CreateDbContext();
        await SetUserScopeAsync(userBDb, "user-b");

        Assert.Empty(await userBDb.CreativeGoals.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AllTenantOwnedBusinessTables_EnforceRowLevelSecurity()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name
            FROM (
                SELECT c.relname AS table_name,
                       c.relrowsecurity,
                       c.relforcerowsecurity,
                       bool_or(a.attname = 'user_id') AS has_user_id,
                       bool_or(a.attname = 'project_id') AS has_project_id
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_attribute a ON a.attrelid = c.oid
                    AND a.attnum > 0
                    AND NOT a.attisdropped
                WHERE n.nspname = 'public'
                  AND c.relkind = 'r'
                  AND c.relname <> '__EFMigrationsHistory'
                GROUP BY c.relname, c.relrowsecurity, c.relforcerowsecurity
            ) tenant_tables
            WHERE (has_user_id OR has_project_id)
              AND (NOT relrowsecurity OR NOT relforcerowsecurity)
            ORDER BY table_name
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var unprotected = new List<string>();
        while (await reader.ReadAsync())
            unprotected.Add(reader.GetString(0));

        Assert.Empty(unprotected);
    }

    [Fact]
    public async Task ConnectionInterceptor_SetsAuthenticatedDatabaseUserScope()
    {
        var interceptor = new UserScopeConnectionInterceptor(new StubCurrentUserService("user-interceptor"));
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new PostgresNovelAgentDbContext(options);
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT current_setting('app.current_user_id', true)";

        Assert.Equal("user-interceptor", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task CollaborationMemory_IsInvisibleAcrossDatabaseUserScopes()
    {
        await using (var userADb = CreateDbContext())
        {
            await SetUserScopeAsync(userADb, "user-a");
            userADb.ProjectCollaborationDecisions.Add(new ProjectCollaborationDecision
            {
                Id = "decision-a",
                UserId = "user-a",
                ProjectId = "shared-project-id",
                MemoryKind = "AestheticDirection",
                ContentJson = "{\"direction\":\"冷峻克制\"}",
                Source = "user"
            });
            await userADb.SaveChangesAsync();
        }

        await using var userBDb = CreateDbContext();
        await SetUserScopeAsync(userBDb, "user-b");

        Assert.Empty(await userBDb.ProjectCollaborationDecisions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OutboxClaimFunction_ClaimsTenantWorkWithoutLeakingPayloadAcrossScopes()
    {
        await using (var adminDb = CreateAdminDbContext())
        {
            adminDb.OutboxEvents.Add(new OutboxEvent
            {
                Id = "outbox-background-claim",
                UserId = "user-background-a",
                ProjectId = "project-a",
                EventType = "project_domain_event",
                AggregateType = "domain_event",
                AggregateId = "aggregate-a",
                IdempotencyKey = "outbox-background-claim",
                PayloadJson = "{\"secret\":\"tenant-a\"}"
            });
            await adminDb.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(_workerConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT event_id, user_id FROM claim_outbox_events(@owner, @lease_seconds, @max_items)";
        command.Parameters.AddWithValue("owner", "test-worker");
        command.Parameters.AddWithValue("lease_seconds", 60);
        command.Parameters.AddWithValue("max_items", 10);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("outbox-background-claim", reader.GetString(0));
        Assert.Equal("user-background-a", reader.GetString(1));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task RuntimeSessionListFunction_ReturnsOnlyMinimalAuthorityCursorsWithoutTenantScope()
    {
        await using (var adminDb = CreateAdminDbContext())
        {
            adminDb.AgentRuntimeRuns.Add(new AgentRuntimeRun
            {
                Id = "runtime-background-list",
                UserId = "user-runtime-a",
                SessionId = "session-runtime-a",
                ProjectId = "project-runtime-a",
                Status = "running",
                IdempotencyKey = "runtime-background-list"
            });
            await adminDb.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(_workerConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT runtime_run_id, user_id, session_id, project_id FROM list_active_runtime_sessions(@max_sessions)";
        command.Parameters.AddWithValue("max_sessions", 10);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("runtime-background-list", reader.GetString(0));
        Assert.Equal("user-runtime-a", reader.GetString(1));
        Assert.Equal("session-runtime-a", reader.GetString(2));
        Assert.Equal("project-runtime-a", reader.GetString(3));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task BackgroundClaimPreflight_VerifiesWorkerIdentityAndAllFunctionGrants()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovelAgentWorkerDb"] = _workerConnectionString
            })
            .Build();
        var preflight = new BackgroundClaimDatabasePreflight(
            new BackgroundClaimConnectionFactory(configuration));

        await preflight.VerifyRoleAsync();
        await preflight.VerifyPermissionsAsync();
    }

    [Fact]
    public async Task Registration_SetsNewTenantScopeBeforeCreatingDefaultSettings()
    {
        await using var db = CreateDbContext();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "registration-test-secret-key-at-least-32-bytes",
                ["JwtSettings:Issuer"] = "tests",
                ["JwtSettings:Audience"] = "tests",
                ["JwtSettings:ExpiryDays"] = "1"
            })
            .Build();
        var service = new AuthService(
            db,
            new JwtTokenGenerator(configuration),
            configuration,
            Mock.Of<IMemoryCacheService>(),
            NullLogger<AuthService>.Instance);

        var response = await service.RegisterAsync(new RegisterRequest
        {
            Username = "rls-registration-user",
            Email = "rls-registration@test.local",
            Password = "StrongPassword123!"
        });

        await using var verify = CreateAdminDbContext();
        Assert.True(await verify.UserSettings.AsNoTracking().AnyAsync(item => item.UserId == response.User.Id));
    }

    [Fact]
    public async Task RuntimeMaintenanceFunctions_ClaimAndCleanUpWithoutTenantScope()
    {
        await using (var adminDb = CreateAdminDbContext())
        {
            adminDb.AgentRuntimeRuns.AddRange(
                new AgentRuntimeRun
                {
                    Id = "runtime-queued-maintenance",
                    UserId = "user-maintenance",
                    SessionId = "session-queued",
                    Status = "queued",
                    IdempotencyKey = "runtime-queued-maintenance"
                },
                new AgentRuntimeRun
                {
                    Id = "runtime-stale-maintenance",
                    UserId = "user-maintenance",
                    SessionId = "session-stale",
                    Status = "running",
                    IdempotencyKey = "runtime-stale-maintenance",
                    UpdatedAt = DateTime.UtcNow.AddHours(-2)
                });
            adminDb.AgentToolExecutions.Add(new AgentToolExecution
            {
                Id = "tool-running-maintenance",
                UserId = "user-maintenance",
                SessionId = "session-stale",
                ToolName = "inspect_project",
                ArgumentsHash = new string('a', 64),
                Status = "running",
                StartedAt = DateTime.UtcNow.AddMinutes(-5)
            });
            await adminDb.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(_workerConnectionString);
        await connection.OpenAsync();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT runtime_run_id FROM list_queued_runtime_runs(@max_items)";
            list.Parameters.AddWithValue("max_items", 10);
            Assert.Equal("runtime-queued-maintenance", await list.ExecuteScalarAsync());
        }
        await using (var claim = connection.CreateCommand())
        {
            claim.CommandText = "SELECT user_id FROM claim_runtime_run(@runtime_run_id)";
            claim.Parameters.AddWithValue("runtime_run_id", "runtime-queued-maintenance");
            Assert.Equal("user-maintenance", await claim.ExecuteScalarAsync());
        }
        await using (var stale = connection.CreateCommand())
        {
            stale.CommandText = "SELECT runtime_run_id FROM fail_stale_runtime_runs(@stale_seconds, @reason)";
            stale.Parameters.AddWithValue("stale_seconds", 1800);
            stale.Parameters.AddWithValue("reason", "stale test");
            Assert.Equal("runtime-stale-maintenance", await stale.ExecuteScalarAsync());
        }
        await using (var tools = connection.CreateCommand())
        {
            tools.CommandText = "SELECT execution_id FROM fail_running_tool_executions(@reason)";
            tools.Parameters.AddWithValue("reason", "restart test");
            Assert.Equal("tool-running-maintenance", await tools.ExecuteScalarAsync());
        }
    }

    [Theory]
    [InlineData("SELECT task_id FROM claim_kernel_task('request-path', 60)")]
    [InlineData("SELECT task_id FROM claim_knowledge_processing_task('request-path', 60)")]
    [InlineData("SELECT execution_id FROM claim_stale_model_execution(120)")]
    [InlineData("SELECT execution_id FROM settle_next_model_execution()")]
    [InlineData("SELECT execution_id FROM fail_stale_tool_executions(1800, 'stale test')")]
    [InlineData("SELECT runtime_run_id FROM list_active_runtime_session_page(1, NULL, NULL)")]
    public async Task BackgroundClaimFunctions_RejectConnectionsWithTenantScope(string sql)
    {
        await using var connection = new NpgsqlConnection(_workerConnectionString);
        await connection.OpenAsync();
        await using (var scope = connection.CreateCommand())
        {
            scope.CommandText = "SELECT set_config('app.current_user_id', 'request-user', false)";
            await scope.ExecuteNonQueryAsync();
        }

        await using var claim = connection.CreateCommand();
        claim.CommandText = sql;

        var exception = await Assert.ThrowsAsync<PostgresException>(() => claim.ExecuteScalarAsync());
        Assert.Equal("P0001", exception.SqlState);
        Assert.Contains("restricted to background connections without tenant scope", exception.MessageText);
    }

    [Theory]
    [InlineData("SELECT task_id FROM claim_kernel_task('request-path', 60)")]
    [InlineData("SELECT task_id FROM claim_knowledge_processing_task('request-path', 60)")]
    [InlineData("SELECT execution_id FROM claim_stale_model_execution(120)")]
    [InlineData("SELECT execution_id FROM settle_next_model_execution()")]
    [InlineData("SELECT execution_id FROM fail_stale_tool_executions(1800, 'stale test')")]
    [InlineData("SELECT event_id FROM claim_outbox_events('request-path', 60, 1)")]
    [InlineData("SELECT runtime_run_id FROM list_active_runtime_sessions(1)")]
    [InlineData("SELECT runtime_run_id FROM list_active_runtime_session_page(1, NULL, NULL)")]
    public async Task BackgroundClaimFunctions_DenyHttpApplicationRole(string sql)
    {
        await using var connection = new NpgsqlConnection(_applicationConnectionString);
        await connection.OpenAsync();
        await using var claim = connection.CreateCommand();
        claim.CommandText = sql;

        var exception = await Assert.ThrowsAsync<PostgresException>(() => claim.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    private PostgresNovelAgentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_applicationConnectionString)
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }

    private PostgresNovelAgentDbContext CreateAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }

    private static async Task SetUserScopeAsync(PostgresNovelAgentDbContext db, string userId)
    {
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_user_id', {userId}, false)");
    }

    private sealed class StubCurrentUserService(string userId) : ICurrentUserService
    {
        public string GetUserId() => userId;
        public string GetUsername() => "test";
        public string GetEmail() => "test@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => userId;
    }

    private sealed class BackgroundCurrentUserService(IBackgroundUserContext backgroundUsers) : ICurrentUserService
    {
        public string GetUserId() => backgroundUsers.Current?.UserId
            ?? throw new UnauthorizedAccessException();
        public string GetUsername() => backgroundUsers.Current?.Username ?? "background";
        public string GetEmail() => backgroundUsers.Current?.Email ?? string.Empty;
        public string GetRole() => backgroundUsers.Current?.Role ?? "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => backgroundUsers.Current != null;
        public string? TryGetUserId() => backgroundUsers.Current?.UserId;
    }
}

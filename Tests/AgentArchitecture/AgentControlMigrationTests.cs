using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Tianming.NovelAgent.Infrastructure.Persistence;

namespace Tests.AgentArchitecture;

public sealed class AgentControlMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("agent_migration_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Additive_migration_preserves_existing_ownership_and_rls_scopes_new_tables()
    {
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE creative_goals (
                    id text PRIMARY KEY, user_id text NOT NULL, project_id text NOT NULL,
                    idempotency_key text NOT NULL DEFAULT '');
                CREATE TABLE goal_revisions (
                    id text PRIMARY KEY, user_id text NOT NULL, project_id text NOT NULL);
                CREATE TABLE book_productions (id text PRIMARY KEY);
                CREATE TABLE outbox_events (id text PRIMARY KEY);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var migrationOptions = new DbContextOptionsBuilder<AgentControlDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                postgres => postgres.MigrationsHistoryTable("__AgentControlMigrationsHistory"))
            .Options;
        await using (var migrationDb = new AgentControlDbContext(migrationOptions))
            await migrationDb.Database.MigrateAsync();

        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    to_regclass('agent_conversation_messages') IS NOT NULL,
                    to_regclass('agent_stream_events') IS NOT NULL,
                    to_regclass('agent_canon_write_leases') IS NOT NULL,
                    to_regclass('canon_branches') IS NULL,
                    to_regclass('knowledge_bases') IS NULL,
                    to_regclass('chapters') IS NULL,
                    obj_description(
                        'claim_kernel_task(text,integer)'::regprocedure,
                        'pg_proc') = 'AgentControlDbContext worker ownership'
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            for (var index = 0; index < 7; index++)
                Assert.True(reader.GetBoolean(index));
        }

        await SeedApplicationRoleAsync();
        var appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = "agent_app",
            Password = "agent_app_password",
            Pooling = false
        }.ConnectionString;
        var userScope = new AgentUserScope();
        var interceptor = new AgentUserScopeConnectionInterceptor(userScope);
        var appOptions = new DbContextOptionsBuilder<AgentControlDbContext>()
            .UseNpgsql(appConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        using (userScope.Enter("user-a"))
        await using (var userADb = new AgentControlDbContext(appOptions))
        {
            var visible = await userADb.ConversationMessages.AsNoTracking().Select(x => x.UserId).ToListAsync();
            Assert.Equal(["user-a"], visible);
        }

        using (userScope.Enter("user-b"))
        await using (var userBDb = new AgentControlDbContext(appOptions))
        {
            var visible = await userBDb.ConversationMessages.AsNoTracking().Select(x => x.UserId).ToListAsync();
            Assert.Equal(["user-b"], visible);
        }

        await using (var noScopeDb = new AgentControlDbContext(appOptions))
            Assert.Empty(await noScopeDb.ConversationMessages.AsNoTracking().ToListAsync());
    }

    private async Task SeedApplicationRoleAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE ROLE agent_app LOGIN PASSWORD 'agent_app_password' NOSUPERUSER NOBYPASSRLS;
            GRANT CONNECT ON DATABASE agent_migration_test TO agent_app;
            GRANT USAGE ON SCHEMA public TO agent_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON agent_conversation_messages TO agent_app;
            INSERT INTO agent_conversation_messages
                (id, user_id, project_id, session_id, role, content, created_at)
            VALUES
                ('message-a', 'user-a', 'project-a', 'session-a', 'user', 'a', CURRENT_TIMESTAMP),
                ('message-b', 'user-b', 'project-b', 'session-b', 'user', 'b', CURRENT_TIMESTAMP);
            """;
        await command.ExecuteNonQueryAsync();
    }
}

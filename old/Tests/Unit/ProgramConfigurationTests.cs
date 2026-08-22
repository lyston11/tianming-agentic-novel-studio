using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using TM.Web.NovelAgentWeb.Data;
using Xunit;

namespace Tests.Unit;

public class ProgramConfigurationTests
{
    [Fact]
    public void ProductionProject_IncludesPostgresProvider()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var projectFile = File.ReadAllText(
            Path.Combine(repoRoot, "Web/NovelAgentWeb/NovelAgentWeb.csproj"));

        Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", projectFile);
    }

    [Fact]
    public void Program_UsesNpgsqlForTheAuthoritativeDatabase()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var programSource = File.ReadAllText(Path.Combine(repoRoot, "Web/NovelAgentWeb/Program.cs"));

        Assert.Contains(".UseNpgsql", programSource);
        Assert.DoesNotContain("options.UseSqlite", programSource);
    }

    [Fact]
    public void Program_UsesDedicatedPostgresMigrationContext()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var programSource = File.ReadAllText(Path.Combine(repoRoot, "Web/NovelAgentWeb/Program.cs"));

        Assert.Contains("AddDbContext<PostgresNovelAgentDbContext>", programSource);
        Assert.Contains(
            "AddScoped<NovelAgentDbContext>(sp => sp.GetRequiredService<PostgresNovelAgentDbContext>())",
            programSource);
        Assert.Contains("GetConnectionString(\"NovelAgentMigrationDb\")", programSource);
        Assert.DoesNotContain("chapterIdentityMigration.NormalizeAsync()", programSource);
    }

    [Fact]
    public void Program_VerifiesWorkerRoleBeforeMigrationAndPermissionsAfterMigration()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var programSource = File.ReadAllText(Path.Combine(repoRoot, "Web/NovelAgentWeb/Program.cs"));

        var rolePreflight = programSource.IndexOf("VerifyRoleAsync", StringComparison.Ordinal);
        var migrate = programSource.IndexOf("migrationDb.Database.MigrateAsync", StringComparison.Ordinal);
        var permissionPreflight = programSource.IndexOf("VerifyPermissionsAsync", StringComparison.Ordinal);
        Assert.True(rolePreflight >= 0 && rolePreflight < migrate);
        Assert.True(permissionPreflight > migrate);
    }

    [Fact]
    public void BackgroundClaimConnectionFactory_RejectsApplicationRoleConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovelAgentWorkerDb"] =
                    "Host=localhost;Database=novelagent;Username=novelagent_app;Password=secret"
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => new BackgroundClaimConnectionFactory(configuration));

        Assert.Contains("novelagent_worker", error.Message);
    }

    [Fact]
    public void StandardConfiguration_DoesNotStoreDatabaseCredentials()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var config = new ConfigurationBuilder()
            .SetBasePath(repoRoot)
            .AddJsonFile("Web/NovelAgentWeb/appsettings.json", optional: false)
            .Build();

        Assert.Null(config.GetConnectionString("NovelAgentDb"));
        Assert.Null(config.GetConnectionString("NovelAgentWorkerDb"));
        Assert.Null(config.GetConnectionString("NovelAgentMigrationDb"));
    }

    [Fact]
    public void DockerCompose_ProvidesHealthyPostgresToTheApi()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var compose = File.ReadAllText(Path.Combine(repoRoot, "docker-compose.yml"));

        Assert.Contains("postgres:16-alpine", compose);
        Assert.Contains("pg_isready", compose);
        Assert.Contains("ConnectionStrings__NovelAgentDb=Host=postgres;Port=5432;Database=novelagent", compose);
        Assert.Contains("POSTGRES_USER=novelagent_admin", compose);
        Assert.Contains("Username=novelagent_app", compose);
        Assert.Contains("ConnectionStrings__NovelAgentWorkerDb=Host=postgres;Port=5432;Database=novelagent;Username=novelagent_worker", compose);
        Assert.Contains("ConnectionStrings__NovelAgentMigrationDb=Host=postgres;Port=5432;Database=novelagent;Username=novelagent_admin", compose);
        Assert.DoesNotContain("ConnectionStrings__NovelAgentDb=Host=postgres;Port=5432;Database=novelagent;Username=novelagent_admin", compose);
        Assert.Contains("001-create-app-users.sh:/docker-entrypoint-initdb.d/001-create-app-users.sh:ro", compose);
        Assert.Contains("postgres-data:/var/lib/postgresql/data", compose);
        Assert.Contains("condition: service_healthy", compose);
    }

    [Fact]
    public void DockerCompose_RequiresExternalSecretsAndDoesNotPublishPostgresPort()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var compose = File.ReadAllText(Path.Combine(repoRoot, "docker-compose.yml"));
        var settings = File.ReadAllText(Path.Combine(repoRoot, "Web/NovelAgentWeb/appsettings.json"));

        Assert.Contains("${NOVELAGENT_ADMIN_PASSWORD:?", compose);
        Assert.Contains("${NOVELAGENT_APP_PASSWORD:?", compose);
        Assert.Contains("${NOVELAGENT_WORKER_PASSWORD:?", compose);
        Assert.Contains("${NOVELAGENT_JWT_SECRET:?", compose);
        Assert.DoesNotContain("POSTGRES_PASSWORD=novelagent_admin", compose);
        Assert.DoesNotContain("Password=novelagent_app", compose);
        Assert.DoesNotContain("Password=novelagent_worker", compose);
        Assert.DoesNotContain("Password=novelagent_admin", compose);
        Assert.DoesNotContain("\"5432:5432\"", compose);
        Assert.Contains("postgres-role-bootstrap:", compose);
        Assert.Contains("condition: service_completed_successfully", compose);
        Assert.DoesNotContain("YourSuperSecretKeyForJWTTokenGeneration", settings);
        Assert.DoesNotContain("Password=novelagent_", settings);
    }

    [Fact]
    public void StandardConfiguration_UsesRequiredRuntimePortsAndRedis()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);

        var config = new ConfigurationBuilder()
            .SetBasePath(repoRoot)
            .AddJsonFile("Web/NovelAgentWeb/appsettings.json", optional: false)
            .Build();

        Assert.Equal("true", config["Redis:Enabled"], ignoreCase: true);
        Assert.Equal("localhost:6379", config["Redis:ConnectionString"]);
        Assert.Equal("NovelAgent:", config["Redis:InstanceName"]);
        Assert.Equal("00:10:00", config["Redis:DefaultExpiration"]);
        Assert.Null(config["Redis:AllowInMemoryFallback"]);
        Assert.Equal("http://localhost:6333", config["Qdrant:BaseUrl"]);
        Assert.Equal("6334", config["Qdrant:Port"]);
    }

    [Fact]
    public void DockerCompose_ApiUsesQdrantHttpBaseUrlAndGrpcPortSeparately()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var compose = File.ReadAllText(Path.Combine(repoRoot, "docker-compose.yml"));

        Assert.Contains("Qdrant__BaseUrl=http://qdrant:6333", compose);
        Assert.Contains("Qdrant__Host=qdrant", compose);
        Assert.Contains("Qdrant__Port=6334", compose);
        Assert.DoesNotContain("Qdrant__BaseUrl=http://qdrant:6334", compose);
    }

    [Fact]
    public void Program_DoesNotRegisterContentDocumentServiceTwice()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var programSource = File.ReadAllText(Path.Combine(repoRoot, "Web/NovelAgentWeb/Program.cs"));

        var registrations = Regex.Matches(
            programSource,
            @"AddScoped<IContentDocumentService,\s*ContentDocumentService>\(");

        Assert.Single(registrations);
    }

    [Fact]
    public void Program_RegistersOutputArtifactRecorder()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var programSource = File.ReadAllText(Path.Combine(repoRoot, "Web/NovelAgentWeb/Program.cs"));

        Assert.Contains("AddScoped<IOutputArtifactRecorder, OutputArtifactRecorder>", programSource);
    }

    [Fact]
    public void Program_PersistsDataProtectionKeysForContainerRuntime()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var programSource = File.ReadAllText(Path.Combine(repoRoot, "Web/NovelAgentWeb/Program.cs"));

        Assert.Contains("App_Data", programSource);
        Assert.Contains("DataProtectionKeys", programSource);
        Assert.Contains("PersistKeysToFileSystem", programSource);
        Assert.Contains("SetApplicationName(\"NovelAgentWeb\")", programSource);
    }

    [Fact]
    public void Program_UsesTargetArchitectureDirectorForForegroundTurns()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var programSource = File.ReadAllText(Path.Combine(repoRoot, "Web/NovelAgentWeb/Program.cs"));

        Assert.Contains("AddScoped<TargetArchitectureDirector>()", programSource);
        Assert.Contains(
            "AddScoped<IAgentForegroundTurnRunner>(sp => sp.GetRequiredService<TargetArchitectureDirector>())",
            programSource);
        Assert.DoesNotContain("AddScoped<AgentRuntime>()", programSource);
        Assert.DoesNotContain("AddSingleton<AgentPlanner>()", programSource);
        Assert.DoesNotContain("AddSingleton<ReflectionEngine>()", programSource);
    }

    [Fact]
    public void TargetArchitectureDirector_DoesNotCallPeerBusinessToolsOrKernels()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var directorSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "Web/NovelAgentWeb/Services/Goals/TargetArchitectureDirector.cs"));

        Assert.DoesNotContain("AgentToolRegistry", directorSource);
        Assert.DoesNotContain("IKernel", directorSource);
        Assert.DoesNotContain("KernelRegistry", directorSource);
        Assert.DoesNotContain("AgentPlanner", directorSource);
        Assert.DoesNotContain("ReflectionEngine", directorSource);
    }

    [Fact]
    public void Program_UsesEfMigrationsAsTheOnlyStartupSchemaMutation()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var programSource = File.ReadAllText(Path.Combine(repoRoot, "Web/NovelAgentWeb/Program.cs"));

        var migrateIndex = programSource.IndexOf("Database.Migrate", StringComparison.Ordinal);
        var normalizeIndex = programSource.IndexOf("SqliteSchemaNormalizer.Normalize", StringComparison.Ordinal);
        var runIndex = programSource.IndexOf("app.Run()", StringComparison.Ordinal);

        Assert.True(migrateIndex >= 0, "Program must apply EF migrations before hosted services query new tables.");
        Assert.Equal(-1, normalizeIndex);
        Assert.True(runIndex > migrateIndex, "Database migration must happen before app.Run starts hosted services.");
    }

    [Fact]
    public async Task SqliteSchemaNormalizer_AddsMissingAgentLoopColumnsToExistingUserSettingsTable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE user_settings (
                    user_id TEXT NOT NULL PRIMARY KEY,
                    llm_provider TEXT NULL,
                    llm_api_key_encrypted TEXT NULL,
                    llm_base_url TEXT NULL,
                    llm_model TEXT NULL
                );

                INSERT INTO user_settings (user_id, llm_provider)
                VALUES ('admin-user', 'openai');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);

        SqliteSchemaNormalizer.Normalize(db);

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(user_settings)";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(1));
        }

        Assert.Contains("agent_default_risk", columns);
        Assert.Contains("agent_loop_auto_proceed", columns);
        Assert.Contains("agent_loop_max_steps", columns);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT agent_default_risk, agent_loop_auto_proceed, agent_loop_max_steps
                FROM user_settings
                WHERE user_id = 'admin-user'
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Medium", reader.GetString(0));
            Assert.Equal(1L, reader.GetInt64(1));
            Assert.Equal(12L, reader.GetInt64(2));
        }
    }

    [Fact]
    public async Task SqliteSchemaNormalizer_CreatesMissingMemoryAuditTablesForLegacyRuntimeDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE users (
                    id TEXT NOT NULL PRIMARY KEY
                );

                CREATE TABLE novel_projects (
                    id TEXT NOT NULL PRIMARY KEY
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);

        SqliteSchemaNormalizer.Normalize(db);

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        Assert.Contains("agent_memory_reads", tables);
        Assert.Contains("agent_memory_promotions", tables);

        var readColumns = await ReadColumnsAsync(connection, "agent_memory_reads");
        Assert.Contains("run_id", readColumns);
        Assert.Contains("memory_keys_json", readColumns);
        Assert.Contains("consumer", readColumns);

        var promotionColumns = await ReadColumnsAsync(connection, "agent_memory_promotions");
        Assert.Contains("source_scope", promotionColumns);
        Assert.Contains("target_memory_key", promotionColumns);
        Assert.Contains("payload_json", promotionColumns);
    }

    [Fact]
    public async Task SqliteSchemaNormalizer_CreatesMissingProductionTruthTablesForLegacyRuntimeDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE users (
                    id TEXT NOT NULL PRIMARY KEY
                );

                CREATE TABLE novel_projects (
                    id TEXT NOT NULL PRIMARY KEY
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);

        SqliteSchemaNormalizer.Normalize(db);

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        Assert.Contains("chapter_changes", tables);
        Assert.Contains("chapter_drafts", tables);
        Assert.Contains("generation_gate_reports", tables);
        Assert.Contains("agent_reviews", tables);

        var chapterChangeColumns = await ReadColumnsAsync(connection, "chapter_changes");
        Assert.Contains("parse_status", chapterChangeColumns);
        Assert.Contains("applied_to_fact_snapshot", chapterChangeColumns);

        var chapterDraftColumns = await ReadColumnsAsync(connection, "chapter_drafts");
        Assert.Contains("draft_content", chapterDraftColumns);
        Assert.Contains("repair_attempt_count", chapterDraftColumns);

        var gateColumns = await ReadColumnsAsync(connection, "generation_gate_reports");
        Assert.Contains("fact_snapshot_passed", gateColumns);
        Assert.Contains("repair_hint_count", gateColumns);

        var reviewColumns = await ReadColumnsAsync(connection, "agent_reviews");
        Assert.Contains("review_json", reviewColumns);
        Assert.Contains("requires_rewrite", reviewColumns);
        Assert.Contains("meets_accepted_creative_intents", reviewColumns);
        Assert.Contains("continuity_risk", reviewColumns);
        Assert.Contains("chapter_pacing", reviewColumns);
        Assert.Contains("recommended_action", reviewColumns);
    }

    [Fact]
    public async Task SqliteSchemaNormalizer_AddsMissingAgentReviewDecisionColumnsToExistingProductionTruthTable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE users (
                    id TEXT NOT NULL PRIMARY KEY
                );

                CREATE TABLE novel_projects (
                    id TEXT NOT NULL PRIMARY KEY
                );

                CREATE TABLE agent_reviews (
                    id TEXT NOT NULL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    project_id TEXT NOT NULL,
                    runtime_run_id TEXT NOT NULL,
                    chapter_id TEXT NOT NULL,
                    package_id TEXT NULL,
                    review_id TEXT NOT NULL,
                    overall_result TEXT NOT NULL DEFAULT 'Unknown',
                    validation_overall_result TEXT NOT NULL,
                    requires_rewrite INTEGER NOT NULL,
                    quality_score INTEGER NOT NULL,
                    content_length INTEGER NOT NULL,
                    check_count INTEGER NOT NULL,
                    summary TEXT NOT NULL,
                    review_json TEXT NOT NULL,
                    reviewed_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);

        SqliteSchemaNormalizer.Normalize(db);

        var reviewColumns = await ReadColumnsAsync(connection, "agent_reviews");
        Assert.Contains("meets_accepted_creative_intents", reviewColumns);
        Assert.Contains("continuity_risk", reviewColumns);
        Assert.Contains("chapter_pacing", reviewColumns);
        Assert.Contains("recommended_action", reviewColumns);
    }

    [Fact]
    public async Task SqliteSchemaNormalizer_AddsMissingOutboxProcessingLeaseColumnsToExistingOutboxTable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE outbox_events (
                    id TEXT NOT NULL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    project_id TEXT NULL,
                    runtime_run_id TEXT NULL,
                    event_type TEXT NOT NULL,
                    aggregate_type TEXT NOT NULL,
                    aggregate_id TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'pending',
                    attempts INTEGER NOT NULL DEFAULT 0,
                    last_error TEXT NULL,
                    next_attempt_at TEXT NULL,
                    completed_at TEXT NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);

        SqliteSchemaNormalizer.Normalize(db);

        var outboxColumns = await ReadColumnsAsync(connection, "outbox_events");
        Assert.Contains("processing_owner", outboxColumns);
        Assert.Contains("processing_lease_expires_at", outboxColumns);
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));

        return columns;
    }
}

using System.Data;
using Microsoft.EntityFrameworkCore;

namespace TM.Web.NovelAgentWeb.Data;

public static class SqliteSchemaNormalizer
{
    private sealed record MissingColumn(string Name, string Definition);

    private static readonly MissingColumn[] UserSettingsColumns =
    [
        new("agent_default_risk", "TEXT NOT NULL DEFAULT 'Medium'"),
        new("agent_loop_auto_proceed", "INTEGER NOT NULL DEFAULT 1"),
        new("agent_loop_max_steps", "INTEGER NOT NULL DEFAULT 12"),
    ];

    private static readonly MissingColumn[] AgentReviewDecisionColumns =
    [
        new("meets_accepted_creative_intents", "INTEGER NOT NULL DEFAULT 1"),
        new("continuity_risk", "TEXT NOT NULL DEFAULT ''"),
        new("chapter_pacing", "TEXT NOT NULL DEFAULT ''"),
        new("recommended_action", "TEXT NOT NULL DEFAULT ''"),
    ];

    private static readonly MissingColumn[] RevisionPlanDisplayIdentityColumns =
    [
        new("target_chapter_logical_id", "TEXT NULL"),
        new("target_chapter_display_name", "TEXT NULL"),
    ];

    public static void Normalize(NovelAgentDbContext db)
    {
        if (!db.Database.IsSqlite())
            return;

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
            connection.Open();

        try
        {
            EnsureUserSettingsColumns(connection);
            EnsureProductionTruthTables(connection);
            EnsureMemoryAuditTables(connection);
        }
        finally
        {
            if (shouldClose)
                connection.Close();
        }
    }

    private static void EnsureUserSettingsColumns(IDbConnection connection)
    {
        if (!TableExists(connection, "user_settings"))
            return;

        EnsureMissingColumns(connection, "user_settings", UserSettingsColumns);
    }

    private static void EnsureProductionTruthTables(IDbConnection connection)
    {
        EnsureRevisionPlansColumns(connection);
        EnsureChapterChangesTable(connection);
        EnsureChapterDraftsTable(connection);
        EnsureGenerationGateReportsTable(connection);
        EnsureAgentReviewsTable(connection);
    }

    private static void EnsureRevisionPlansColumns(IDbConnection connection)
    {
        if (!TableExists(connection, "revision_plans"))
            return;

        EnsureMissingColumns(connection, "revision_plans", RevisionPlanDisplayIdentityColumns);
    }

    private static void EnsureChapterChangesTable(IDbConnection connection)
    {
        if (!TableExists(connection, "chapter_changes"))
        {
            using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE chapter_changes (
                    id TEXT NOT NULL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    project_id TEXT NOT NULL,
                    runtime_run_id TEXT NOT NULL,
                    chapter_id TEXT NOT NULL,
                    package_id TEXT NULL,
                    changes_json TEXT NOT NULL,
                    canonical_changes_json TEXT NOT NULL,
                    parse_status TEXT NOT NULL DEFAULT 'parsed',
                    parse_error TEXT NULL,
                    applied_to_fact_snapshot INTEGER NOT NULL DEFAULT 0,
                    applied_at TEXT NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_chapter_changes_novel_projects_project_id
                        FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
                )
                """;
            create.ExecuteNonQuery();
        }

        EnsureIndex(connection, "idx_chapter_changes_project_chapter_created",
            "CREATE INDEX idx_chapter_changes_project_chapter_created ON chapter_changes (project_id, chapter_id, created_at)");
        EnsureIndex(connection, "idx_chapter_changes_project_parse_status",
            "CREATE INDEX idx_chapter_changes_project_parse_status ON chapter_changes (project_id, parse_status)");
        EnsureIndex(connection, "idx_chapter_changes_run_created",
            "CREATE INDEX idx_chapter_changes_run_created ON chapter_changes (runtime_run_id, created_at)");
    }

    private static void EnsureChapterDraftsTable(IDbConnection connection)
    {
        if (!TableExists(connection, "chapter_drafts"))
        {
            using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE chapter_drafts (
                    id TEXT NOT NULL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    project_id TEXT NOT NULL,
                    runtime_run_id TEXT NOT NULL,
                    chapter_id TEXT NOT NULL,
                    package_id TEXT NULL,
                    artifact_id TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'draft_generated',
                    draft_content TEXT NOT NULL,
                    changes_json TEXT NULL,
                    content_length INTEGER NOT NULL,
                    repair_attempt_count INTEGER NOT NULL,
                    has_changes INTEGER NOT NULL,
                    generated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_chapter_drafts_novel_projects_project_id
                        FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
                )
                """;
            create.ExecuteNonQuery();
        }

        EnsureIndex(connection, "idx_chapter_drafts_project_chapter_created",
            "CREATE INDEX idx_chapter_drafts_project_chapter_created ON chapter_drafts (project_id, chapter_id, created_at)");
        EnsureIndex(connection, "idx_chapter_drafts_project_status",
            "CREATE INDEX idx_chapter_drafts_project_status ON chapter_drafts (project_id, status)");
        EnsureIndex(connection, "idx_chapter_drafts_run_created",
            "CREATE INDEX idx_chapter_drafts_run_created ON chapter_drafts (runtime_run_id, created_at)");
    }

    private static void EnsureGenerationGateReportsTable(IDbConnection connection)
    {
        if (!TableExists(connection, "generation_gate_reports"))
        {
            using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE generation_gate_reports (
                    id TEXT NOT NULL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    project_id TEXT NOT NULL,
                    runtime_run_id TEXT NOT NULL,
                    chapter_id TEXT NOT NULL,
                    package_id TEXT NULL,
                    artifact_id TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'pending',
                    report_json TEXT NOT NULL,
                    protocol_passed INTEGER NOT NULL,
                    changes_detected INTEGER NOT NULL,
                    fact_snapshot_passed INTEGER NOT NULL,
                    blueprint_passed INTEGER NOT NULL,
                    rag_passed INTEGER NOT NULL,
                    issue_count INTEGER NOT NULL,
                    repair_hint_count INTEGER NOT NULL,
                    validated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_generation_gate_reports_novel_projects_project_id
                        FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
                )
                """;
            create.ExecuteNonQuery();
        }

        EnsureIndex(connection, "idx_generation_gate_reports_project_chapter_created",
            "CREATE INDEX idx_generation_gate_reports_project_chapter_created ON generation_gate_reports (project_id, chapter_id, created_at)");
        EnsureIndex(connection, "idx_generation_gate_reports_project_status",
            "CREATE INDEX idx_generation_gate_reports_project_status ON generation_gate_reports (project_id, status)");
        EnsureIndex(connection, "idx_generation_gate_reports_run_created",
            "CREATE INDEX idx_generation_gate_reports_run_created ON generation_gate_reports (runtime_run_id, created_at)");
    }

    private static void EnsureAgentReviewsTable(IDbConnection connection)
    {
        if (!TableExists(connection, "agent_reviews"))
        {
            using var create = connection.CreateCommand();
            create.CommandText = """
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
                    meets_accepted_creative_intents INTEGER NOT NULL DEFAULT 1,
                    continuity_risk TEXT NOT NULL DEFAULT '',
                    chapter_pacing TEXT NOT NULL DEFAULT '',
                    recommended_action TEXT NOT NULL DEFAULT '',
                    review_json TEXT NOT NULL,
                    reviewed_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_agent_reviews_novel_projects_project_id
                        FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
                )
                """;
            create.ExecuteNonQuery();
        }

        EnsureMissingColumns(connection, "agent_reviews", AgentReviewDecisionColumns);

        EnsureIndex(connection, "idx_agent_reviews_project_chapter_created",
            "CREATE INDEX idx_agent_reviews_project_chapter_created ON agent_reviews (project_id, chapter_id, created_at)");
        EnsureIndex(connection, "idx_agent_reviews_project_result",
            "CREATE INDEX idx_agent_reviews_project_result ON agent_reviews (project_id, overall_result)");
        EnsureIndex(connection, "idx_agent_reviews_run_created",
            "CREATE INDEX idx_agent_reviews_run_created ON agent_reviews (runtime_run_id, created_at)");
    }

    private static void EnsureMemoryAuditTables(IDbConnection connection)
    {
        EnsureAgentMemoryReadsTable(connection);
        EnsureAgentMemoryPromotionsTable(connection);
    }

    private static void EnsureAgentMemoryReadsTable(IDbConnection connection)
    {
        if (!TableExists(connection, "agent_memory_reads"))
        {
            using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE agent_memory_reads (
                    id TEXT NOT NULL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    project_id TEXT NULL,
                    session_id TEXT NULL,
                    run_id TEXT NULL,
                    memory_scope TEXT NOT NULL,
                    memory_keys_json TEXT NOT NULL DEFAULT '[]',
                    source_type TEXT NOT NULL DEFAULT 'memory_repository',
                    consumer TEXT NOT NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_agent_memory_reads_users_user_id
                        FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
                    CONSTRAINT FK_agent_memory_reads_novel_projects_project_id
                        FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
                )
                """;
            create.ExecuteNonQuery();
        }

        EnsureIndex(connection, "idx_agent_memory_reads_user_created",
            "CREATE INDEX idx_agent_memory_reads_user_created ON agent_memory_reads (user_id, created_at)");
        EnsureIndex(connection, "idx_agent_memory_reads_project_created",
            "CREATE INDEX idx_agent_memory_reads_project_created ON agent_memory_reads (project_id, created_at)");
        EnsureIndex(connection, "idx_agent_memory_reads_session",
            "CREATE INDEX idx_agent_memory_reads_session ON agent_memory_reads (session_id)");
        EnsureIndex(connection, "idx_agent_memory_reads_run",
            "CREATE INDEX idx_agent_memory_reads_run ON agent_memory_reads (run_id)");
    }

    private static void EnsureAgentMemoryPromotionsTable(IDbConnection connection)
    {
        if (!TableExists(connection, "agent_memory_promotions"))
        {
            using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE agent_memory_promotions (
                    id TEXT NOT NULL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    project_id TEXT NULL,
                    session_id TEXT NULL,
                    run_id TEXT NULL,
                    source_scope TEXT NOT NULL,
                    target_scope TEXT NOT NULL,
                    source_memory_key TEXT NOT NULL,
                    target_memory_key TEXT NOT NULL,
                    promotion_reason TEXT NOT NULL,
                    payload_json TEXT NOT NULL DEFAULT '{}',
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_agent_memory_promotions_users_user_id
                        FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
                    CONSTRAINT FK_agent_memory_promotions_novel_projects_project_id
                        FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
                )
                """;
            create.ExecuteNonQuery();
        }

        EnsureIndex(connection, "idx_agent_memory_promotions_user_created",
            "CREATE INDEX idx_agent_memory_promotions_user_created ON agent_memory_promotions (user_id, created_at)");
        EnsureIndex(connection, "idx_agent_memory_promotions_project_created",
            "CREATE INDEX idx_agent_memory_promotions_project_created ON agent_memory_promotions (project_id, created_at)");
        EnsureIndex(connection, "idx_agent_memory_promotions_session",
            "CREATE INDEX idx_agent_memory_promotions_session ON agent_memory_promotions (session_id)");
        EnsureIndex(connection, "idx_agent_memory_promotions_run",
            "CREATE INDEX idx_agent_memory_promotions_run ON agent_memory_promotions (run_id)");
    }

    private static void EnsureIndex(IDbConnection connection, string indexName, string createSql)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = $indexName LIMIT 1";
        var parameter = exists.CreateParameter();
        parameter.ParameterName = "$indexName";
        parameter.Value = indexName;
        exists.Parameters.Add(parameter);
        if (exists.ExecuteScalar() is not null)
            return;

        using var create = connection.CreateCommand();
        create.CommandText = createSql;
        create.ExecuteNonQuery();
    }

    private static void EnsureMissingColumns(
        IDbConnection connection,
        string tableName,
        IReadOnlyList<MissingColumn> columns)
    {
        var existingColumns = ReadColumnNames(connection, tableName);
        foreach (var column in columns)
        {
            if (existingColumns.Contains(column.Name))
                continue;

            using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {column.Name} {column.Definition}";
            command.ExecuteNonQuery();
        }
    }

    private static bool TableExists(IDbConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $tableName LIMIT 1";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$tableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return command.ExecuteScalar() is not null;
    }

    private static HashSet<string> ReadColumnNames(IDbConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";

        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            columns.Add(reader.GetString(1));

        return columns;
    }
}

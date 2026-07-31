using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class AddCollaborationMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "author_memories",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    memory_kind = table.Column<string>(type: "text", nullable: false),
                    content_json = table.Column<string>(type: "jsonb", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_author_memories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "experience_observations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: true),
                    observation_type = table.Column<string>(type: "text", nullable: false),
                    evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    metrics_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experience_observations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "experience_suggestions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    observation_id = table.Column<string>(type: "text", nullable: false),
                    suggestion_type = table.Column<string>(type: "text", nullable: false),
                    proposed_change_json = table.Column<string>(type: "jsonb", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    suppression_fingerprint = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    decision_reason = table.Column<string>(type: "text", nullable: true),
                    effective_goal_id = table.Column<string>(type: "text", nullable: true),
                    decision_version = table.Column<int>(type: "integer", nullable: false),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experience_suggestions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_collaboration_decisions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    memory_kind = table.Column<string>(type: "text", nullable: false),
                    content_json = table.Column<string>(type: "jsonb", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    effective_goal_id = table.Column<string>(type: "text", nullable: true),
                    expires_after_goal = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    source_session_state_id = table.Column<string>(type: "text", nullable: true),
                    source_suggestion_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_collaboration_decisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "session_dialogue_states",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    memory_kind = table.Column<string>(type: "text", nullable: false),
                    content_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_dialogue_states", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_author_memories_kind_version",
                table: "author_memories",
                columns: new[] { "user_id", "memory_kind", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_experience_observations_type",
                table: "experience_observations",
                columns: new[] { "user_id", "project_id", "observation_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_experience_suggestions_status",
                table: "experience_suggestions",
                columns: new[] { "user_id", "project_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_experience_suggestions_suppression",
                table: "experience_suggestions",
                columns: new[] { "user_id", "project_id", "suppression_fingerprint", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_project_collaboration_decisions_active",
                table: "project_collaboration_decisions",
                columns: new[] { "user_id", "project_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_project_collaboration_decisions_suggestion_scope",
                table: "project_collaboration_decisions",
                columns: new[] { "user_id", "source_suggestion_id", "scope" },
                unique: true,
                filter: "source_suggestion_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_session_dialogue_states_scope",
                table: "session_dialogue_states",
                columns: new[] { "user_id", "project_id", "session_id", "status", "created_at" });

            migrationBuilder.Sql("""
                DO $tenant_rls$
                DECLARE
                    target_table text;
                    target_tables text[] := ARRAY[
                        'author_memories',
                        'project_collaboration_decisions',
                        'session_dialogue_states',
                        'experience_observations',
                        'experience_suggestions'
                    ];
                BEGIN
                    FOREACH target_table IN ARRAY target_tables LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', target_table);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', target_table);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I USING (user_id = NULLIF(current_setting(''app.current_user_id'', true), '''')) WITH CHECK (user_id = NULLIF(current_setting(''app.current_user_id'', true), ''''))',
                            target_table);
                    END LOOP;
                END
                $tenant_rls$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "author_memories");

            migrationBuilder.DropTable(
                name: "experience_observations");

            migrationBuilder.DropTable(
                name: "experience_suggestions");

            migrationBuilder.DropTable(
                name: "project_collaboration_decisions");

            migrationBuilder.DropTable(
                name: "session_dialogue_states");
        }
    }
}

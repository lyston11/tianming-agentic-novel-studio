using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres;

[DbContext(typeof(PostgresNovelAgentDbContext))]
[Migration("20260713102731_AddReworkIntents")]
public partial class AddReworkIntents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "rework_intents",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false),
                project_id = table.Column<string>(type: "text", nullable: false),
                goal_id = table.Column<string>(type: "text", nullable: false),
                branch_id = table.Column<string>(type: "text", nullable: false),
                candidate_chapter_id = table.Column<string>(type: "text", nullable: false),
                candidate_version = table.Column<int>(type: "integer", nullable: false),
                session_id = table.Column<string>(type: "text", nullable: false),
                target_scope = table.Column<string>(type: "text", nullable: false),
                selection_start = table.Column<int>(type: "integer", nullable: true),
                selection_end = table.Column<int>(type: "integer", nullable: true),
                selected_text = table.Column<string>(type: "text", nullable: false),
                user_description = table.Column<string>(type: "text", nullable: false),
                problem = table.Column<string>(type: "text", nullable: false),
                desired_effect = table.Column<string>(type: "text", nullable: false),
                preserve_json = table.Column<string>(type: "jsonb", nullable: false),
                may_change_json = table.Column<string>(type: "jsonb", nullable: false),
                must_not_change_json = table.Column<string>(type: "jsonb", nullable: false),
                acceptance_criteria_json = table.Column<string>(type: "jsonb", nullable: false),
                impact_level = table.Column<string>(type: "text", nullable: false),
                impact_assessment_json = table.Column<string>(type: "jsonb", nullable: false),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_rework_intents", x => x.id));

        migrationBuilder.CreateIndex(
            name: "ix_rework_intents_candidate_status",
            table: "rework_intents",
            columns: new[] { "user_id", "candidate_chapter_id", "candidate_version", "status" });

        migrationBuilder.Sql("""
            ALTER TABLE rework_intents ENABLE ROW LEVEL SECURITY;
            ALTER TABLE rework_intents FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON rework_intents
                USING (user_id = NULLIF(current_setting('app.current_user_id', true), ''))
                WITH CHECK (user_id = NULLIF(current_setting('app.current_user_id', true), ''));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "rework_intents");
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260622182000_AddRevisionPlans")]
    public partial class AddRevisionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "revision_plans",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    creative_intent_id = table.Column<string>(type: "TEXT", nullable: true),
                    knowledge_conflict_report_id = table.Column<string>(type: "TEXT", nullable: true),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    runtime_run_id = table.Column<string>(type: "TEXT", nullable: true),
                    idempotency_key = table.Column<string>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "creative_intent"),
                    plan_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "future_carry"),
                    target_scope = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "project"),
                    target_volume_id = table.Column<string>(type: "TEXT", nullable: true),
                    target_chapter_id = table.Column<string>(type: "TEXT", nullable: true),
                    target_chapter_logical_id = table.Column<string>(type: "TEXT", nullable: true),
                    target_chapter_display_name = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "draft"),
                    requirements_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    continuity_requirements_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    impact_analysis_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    affected_chapter_ids_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    invalidated_package_ids_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    risk_level = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "medium"),
                    recommendation = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revision_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_revision_plans_creative_intents_creative_intent_id",
                        column: x => x.creative_intent_id,
                        principalTable: "creative_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_revision_plans_knowledge_conflict_reports_knowledge_conflict_report_id",
                        column: x => x.knowledge_conflict_report_id,
                        principalTable: "knowledge_conflict_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_revision_plans_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_revision_plans_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_revision_plans_conflict_report",
                table: "revision_plans",
                column: "knowledge_conflict_report_id");

            migrationBuilder.CreateIndex(
                name: "idx_revision_plans_creative_intent",
                table: "revision_plans",
                column: "creative_intent_id");

            migrationBuilder.CreateIndex(
                name: "idx_revision_plans_idempotency",
                table: "revision_plans",
                columns: new[] { "user_id", "project_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_revision_plans_project_chapter_status",
                table: "revision_plans",
                columns: new[] { "project_id", "target_chapter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_revision_plans_project_status_created",
                table: "revision_plans",
                columns: new[] { "project_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_revision_plans_user_id",
                table: "revision_plans",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "revision_plans");
        }
    }
}

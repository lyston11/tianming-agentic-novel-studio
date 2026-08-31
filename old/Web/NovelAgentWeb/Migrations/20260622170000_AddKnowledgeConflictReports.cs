using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260622170000_AddKnowledgeConflictReports")]
    public partial class AddKnowledgeConflictReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_conflict_reports",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    knowledge_id = table.Column<string>(type: "TEXT", nullable: false),
                    conflicting_knowledge_ids_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    conflict_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    severity = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    impact_scope = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    explanation = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    recommended_action = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    requires_user_decision = table.Column<bool>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "open"),
                    detection_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    source_session_id = table.Column<string>(type: "TEXT", nullable: true),
                    source_run_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_conflict_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_conflict_reports_knowledge_base_knowledge_id",
                        column: x => x.knowledge_id,
                        principalTable: "knowledge_base",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_conflict_reports_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_conflict_reports_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_conflict_reports_knowledge_id",
                table: "knowledge_conflict_reports",
                column: "knowledge_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_conflict_reports_project_id",
                table: "knowledge_conflict_reports",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_conflict_reports_user_id",
                table: "knowledge_conflict_reports",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_conflict_reports_knowledge",
                table: "knowledge_conflict_reports",
                columns: new[] { "project_id", "knowledge_id" });

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_conflict_reports_project_status",
                table: "knowledge_conflict_reports",
                columns: new[] { "user_id", "project_id", "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_conflict_reports");
        }
    }
}

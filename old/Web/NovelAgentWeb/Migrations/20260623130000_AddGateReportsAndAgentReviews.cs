using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    [Migration("20260623130000_AddGateReportsAndAgentReviews")]
    public partial class AddGateReportsAndAgentReviews : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "generation_gate_reports",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    runtime_run_id = table.Column<string>(type: "TEXT", nullable: false),
                    chapter_id = table.Column<string>(type: "TEXT", nullable: false),
                    package_id = table.Column<string>(type: "TEXT", nullable: true),
                    artifact_id = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    report_json = table.Column<string>(type: "TEXT", nullable: false),
                    protocol_passed = table.Column<bool>(type: "INTEGER", nullable: false),
                    changes_detected = table.Column<bool>(type: "INTEGER", nullable: false),
                    fact_snapshot_passed = table.Column<bool>(type: "INTEGER", nullable: false),
                    blueprint_passed = table.Column<bool>(type: "INTEGER", nullable: false),
                    rag_passed = table.Column<bool>(type: "INTEGER", nullable: false),
                    issue_count = table.Column<int>(type: "INTEGER", nullable: false),
                    repair_hint_count = table.Column<int>(type: "INTEGER", nullable: false),
                    validated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generation_gate_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_generation_gate_reports_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_reviews",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    runtime_run_id = table.Column<string>(type: "TEXT", nullable: false),
                    chapter_id = table.Column<string>(type: "TEXT", nullable: false),
                    package_id = table.Column<string>(type: "TEXT", nullable: true),
                    review_id = table.Column<string>(type: "TEXT", nullable: false),
                    overall_result = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Unknown"),
                    validation_overall_result = table.Column<string>(type: "TEXT", nullable: false),
                    requires_rewrite = table.Column<bool>(type: "INTEGER", nullable: false),
                    quality_score = table.Column<int>(type: "INTEGER", nullable: false),
                    content_length = table.Column<int>(type: "INTEGER", nullable: false),
                    check_count = table.Column<int>(type: "INTEGER", nullable: false),
                    summary = table.Column<string>(type: "TEXT", nullable: false),
                    meets_accepted_creative_intents = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    continuity_risk = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    chapter_pacing = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    recommended_action = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    review_json = table.Column<string>(type: "TEXT", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_reviews_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_generation_gate_reports_project_chapter_created",
                table: "generation_gate_reports",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_generation_gate_reports_project_status",
                table: "generation_gate_reports",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_generation_gate_reports_run_created",
                table: "generation_gate_reports",
                columns: new[] { "runtime_run_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_reviews_project_chapter_created",
                table: "agent_reviews",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_reviews_project_result",
                table: "agent_reviews",
                columns: new[] { "project_id", "overall_result" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_reviews_run_created",
                table: "agent_reviews",
                columns: new[] { "runtime_run_id", "created_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "agent_reviews");
            migrationBuilder.DropTable(name: "generation_gate_reports");
        }
    }
}

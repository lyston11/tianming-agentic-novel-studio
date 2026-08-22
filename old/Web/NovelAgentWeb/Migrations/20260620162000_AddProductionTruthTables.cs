using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260620162000_AddProductionTruthTables")]
    public partial class AddProductionTruthTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    runtime_run_id = table.Column<string>(type: "TEXT", nullable: true),
                    event_type = table.Column<string>(type: "TEXT", nullable: false),
                    aggregate_type = table.Column<string>(type: "TEXT", nullable: false),
                    aggregate_id = table.Column<string>(type: "TEXT", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tianming_packages",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    chapter_id = table.Column<string>(type: "TEXT", nullable: true),
                    runtime_run_id = table.Column<string>(type: "TEXT", nullable: true),
                    package_kind = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "chapter_generation"),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    input_json = table.Column<string>(type: "TEXT", nullable: false),
                    dependency_versions_json = table.Column<string>(type: "TEXT", nullable: true),
                    knowledge_snapshot_json = table.Column<string>(type: "TEXT", nullable: true),
                    fact_snapshot_json = table.Column<string>(type: "TEXT", nullable: true),
                    prompt_version = table.Column<string>(type: "TEXT", nullable: true),
                    kernel_version = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tianming_packages", x => x.id);
                    table.ForeignKey(
                        name: "FK_tianming_packages_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapter_versions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    chapter_id = table.Column<string>(type: "TEXT", nullable: false),
                    content_document_id = table.Column<string>(type: "TEXT", nullable: false),
                    version_number = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    word_count = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "draft"),
                    runtime_run_id = table.Column<string>(type: "TEXT", nullable: true),
                    package_id = table.Column<string>(type: "TEXT", nullable: true),
                    gate_report_json = table.Column<string>(type: "TEXT", nullable: true),
                    agent_review_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_versions_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapter_versions_content_documents_content_document_id",
                        column: x => x.content_document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapter_versions_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "production_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    runtime_run_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    chapter_id = table.Column<string>(type: "TEXT", nullable: true),
                    package_id = table.Column<string>(type: "TEXT", nullable: true),
                    event_type = table.Column<string>(type: "TEXT", nullable: false),
                    stage = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    artifact_type = table.Column<string>(type: "TEXT", nullable: true),
                    artifact_id = table.Column<string>(type: "TEXT", nullable: true),
                    data_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_production_events_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_fact_snapshots",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    chapter_id = table.Column<string>(type: "TEXT", nullable: true),
                    chapter_version_id = table.Column<string>(type: "TEXT", nullable: true),
                    version_number = table.Column<int>(type: "INTEGER", nullable: false),
                    snapshot_json = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "unknown"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_fact_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_fact_snapshots_chapter_versions_chapter_version_id",
                        column: x => x.chapter_version_id,
                        principalTable: "chapter_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_project_fact_snapshots_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_project_fact_snapshots_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_versions_chapter_version",
                table: "chapter_versions",
                columns: new[] { "chapter_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_chapter_versions_document",
                table: "chapter_versions",
                column: "content_document_id");

            migrationBuilder.CreateIndex(
                name: "idx_chapter_versions_project_chapter_created",
                table: "chapter_versions",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_fact_snapshots_project_chapter_version",
                table: "project_fact_snapshots",
                columns: new[] { "project_id", "chapter_id", "version_number" });

            migrationBuilder.CreateIndex(
                name: "idx_fact_snapshots_project_created",
                table: "project_fact_snapshots",
                columns: new[] { "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_project_fact_snapshots_chapter_id",
                table: "project_fact_snapshots",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_fact_snapshots_chapter_version_id",
                table: "project_fact_snapshots",
                column: "chapter_version_id");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_aggregate",
                table: "outbox_events",
                columns: new[] { "aggregate_type", "aggregate_id" });

            migrationBuilder.CreateIndex(
                name: "idx_outbox_status_retry",
                table: "outbox_events",
                columns: new[] { "status", "next_attempt_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_production_events_project_chapter_created",
                table: "production_events",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_production_events_run_created",
                table: "production_events",
                columns: new[] { "runtime_run_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_production_events_chapter_id",
                table: "production_events",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "idx_tianming_packages_project_chapter",
                table: "tianming_packages",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_tianming_packages_run_created",
                table: "tianming_packages",
                columns: new[] { "runtime_run_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_tianming_packages_chapter_id",
                table: "tianming_packages",
                column: "chapter_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "outbox_events");
            migrationBuilder.DropTable(name: "production_events");
            migrationBuilder.DropTable(name: "project_fact_snapshots");
            migrationBuilder.DropTable(name: "tianming_packages");
            migrationBuilder.DropTable(name: "chapter_versions");
        }
    }
}

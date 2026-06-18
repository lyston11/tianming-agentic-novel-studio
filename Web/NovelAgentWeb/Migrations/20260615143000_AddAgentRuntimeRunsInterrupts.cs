using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260615143000_AddAgentRuntimeRunsInterrupts")]
    public partial class AddAgentRuntimeRunsInterrupts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_runtime_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    current_phase = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    current_step = table.Column<int>(type: "INTEGER", nullable: false),
                    active_tool = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    user_message = table.Column<string>(type: "TEXT", nullable: false),
                    last_message = table.Column<string>(type: "TEXT", nullable: false),
                    result_json = table.Column<string>(type: "TEXT", nullable: false),
                    error_message = table.Column<string>(type: "TEXT", nullable: false),
                    cancel_requested = table.Column<bool>(type: "INTEGER", nullable: false),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runtime_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_interrupts",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    runtime_run_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    priority = table.Column<int>(type: "INTEGER", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    decision_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    consumed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_interrupts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_runtime_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    runtime_run_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    type = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    data_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runtime_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_agent_runtime_runs_session_status",
                table: "agent_runtime_runs",
                columns: new[] { "user_id", "session_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_runtime_runs_project_status",
                table: "agent_runtime_runs",
                columns: new[] { "user_id", "project_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_interrupts_run_pending",
                table: "agent_interrupts",
                columns: new[] { "runtime_run_id", "status", "priority", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_interrupts_session_pending",
                table: "agent_interrupts",
                columns: new[] { "user_id", "session_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_runtime_events_session_recent",
                table: "agent_runtime_events",
                columns: new[] { "user_id", "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_runtime_events_run_recent",
                table: "agent_runtime_events",
                columns: new[] { "runtime_run_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "agent_runtime_events");
            migrationBuilder.DropTable(name: "agent_interrupts");
            migrationBuilder.DropTable(name: "agent_runtime_runs");
        }
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260614120000_AddAgentToolExecutions")]
    public partial class AddAgentToolExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_tool_executions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    run_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    tool_name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    phase = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    risk = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    arguments_json = table.Column<string>(type: "TEXT", nullable: false),
                    arguments_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    result_phase = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    result_message = table.Column<string>(type: "TEXT", nullable: false),
                    error_type = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    error_message = table.Column<string>(type: "TEXT", nullable: false),
                    recommended_next_tool = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    missing_prerequisite = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    duration_ms = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_tool_executions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_agent_tool_executions_dedupe",
                table: "agent_tool_executions",
                columns: new[] { "user_id", "project_id", "tool_name", "arguments_hash" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_tool_executions_scope_recent",
                table: "agent_tool_executions",
                columns: new[] { "user_id", "project_id", "session_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_tool_executions");
        }
    }
}

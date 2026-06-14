using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260614170000_AddAgentToolSearchSnapshots")]
    public partial class AddAgentToolSearchSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_tool_search_snapshots",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    phase = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    version = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    tools_json = table.Column<string>(type: "TEXT", nullable: false),
                    source_execution_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    cached_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_tool_search_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_agent_tool_search_snapshots_expires_at",
                table: "agent_tool_search_snapshots",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "idx_agent_tool_search_snapshots_scope_version",
                table: "agent_tool_search_snapshots",
                columns: new[] { "user_id", "project_id", "session_id", "phase", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_tool_search_snapshots");
        }
    }
}

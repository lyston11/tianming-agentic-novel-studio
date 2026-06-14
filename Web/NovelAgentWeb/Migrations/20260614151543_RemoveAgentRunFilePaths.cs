using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAgentRunFilePaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "context_package_path",
                table: "agent_runs");

            migrationBuilder.DropColumn(
                name: "gate_report_path",
                table: "agent_runs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "context_package_path",
                table: "agent_runs",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gate_report_path",
                table: "agent_runs",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }
    }
}

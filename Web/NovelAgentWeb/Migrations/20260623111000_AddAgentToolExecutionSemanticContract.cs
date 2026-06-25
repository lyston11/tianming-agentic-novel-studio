using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260623111000_AddAgentToolExecutionSemanticContract")]
    public partial class AddAgentToolExecutionSemanticContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "semantic_contract_json",
                table: "agent_tool_executions",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "semantic_contract_json",
                table: "agent_tool_executions");
        }
    }
}

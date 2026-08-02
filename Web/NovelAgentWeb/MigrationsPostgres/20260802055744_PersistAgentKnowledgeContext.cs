using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class PersistAgentKnowledgeContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "knowledge_context_json",
                table: "agent_chat_turns",
                type: "text",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "knowledge_context_json",
                table: "agent_chat_turns");
        }
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations;

[DbContext(typeof(NovelAgentDbContext))]
[Migration("20260802060000_PersistAgentKnowledgeContext")]
public sealed class PersistAgentKnowledgeContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "knowledge_context_json",
            table: "agent_chat_turns",
            type: "TEXT",
            nullable: false,
            defaultValue: "{}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "knowledge_context_json",
            table: "agent_chat_turns");
    }
}

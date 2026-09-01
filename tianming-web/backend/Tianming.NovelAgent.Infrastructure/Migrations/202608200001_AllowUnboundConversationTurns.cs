using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tianming.NovelAgent.Infrastructure.Persistence;

#nullable disable

namespace Tianming.NovelAgent.Infrastructure.Migrations;

[DbContext(typeof(AgentControlDbContext))]
[Migration("202608200001_AllowUnboundConversationTurns")]
public sealed class AllowUnboundConversationTurns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE IF EXISTS agent_conversation_messages
                ALTER COLUMN project_id DROP NOT NULL;
            ALTER TABLE IF EXISTS agent_conversation_turns
                ALTER COLUMN project_id DROP NOT NULL;
            ALTER TABLE IF EXISTS agent_conversation_runtime_checkpoints
                ALTER COLUMN project_id DROP NOT NULL;
            ALTER TABLE IF EXISTS domain_events
                ALTER COLUMN project_id DROP NOT NULL;
            ALTER TABLE IF EXISTS agent_stream_events
                ALTER COLUMN project_id DROP NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE IF EXISTS agent_conversation_messages
                ALTER COLUMN project_id SET NOT NULL;
            ALTER TABLE IF EXISTS agent_conversation_turns
                ALTER COLUMN project_id SET NOT NULL;
            ALTER TABLE IF EXISTS agent_conversation_runtime_checkpoints
                ALTER COLUMN project_id SET NOT NULL;
            ALTER TABLE IF EXISTS domain_events
                ALTER COLUMN project_id SET NOT NULL;
            ALTER TABLE IF EXISTS agent_stream_events
                ALTER COLUMN project_id SET NOT NULL;
            """);
    }
}

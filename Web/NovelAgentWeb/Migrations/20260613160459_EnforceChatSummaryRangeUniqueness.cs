using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260613160459_EnforceChatSummaryRangeUniqueness")]
    public partial class EnforceChatSummaryRangeUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_summaries_range_global",
                table: "agent_chat_summaries",
                columns: new[] { "user_id", "session_id", "summary_type", "start_turn", "end_turn" },
                unique: true,
                filter: "project_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_summaries_range_project",
                table: "agent_chat_summaries",
                columns: new[] { "user_id", "project_id", "session_id", "summary_type", "start_turn", "end_turn" },
                unique: true,
                filter: "project_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agent_chat_summaries_range_global",
                table: "agent_chat_summaries");

            migrationBuilder.DropIndex(
                name: "IX_agent_chat_summaries_range_project",
                table: "agent_chat_summaries");
        }
    }
}

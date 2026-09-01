using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260712093000_AddOutboxIdempotency")]
    public partial class AddOutboxIdempotency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "outbox_events",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE outbox_events SET idempotency_key = id WHERE idempotency_key = '';");

            migrationBuilder.CreateIndex(
                name: "ux_outbox_idempotency",
                table: "outbox_events",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key <> ''");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ux_outbox_idempotency", table: "outbox_events");
            migrationBuilder.DropColumn(name: "idempotency_key", table: "outbox_events");
        }
    }
}

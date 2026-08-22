using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260625033000_AddOutboxProcessingLease")]
    public partial class AddOutboxProcessingLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "processing_owner",
                table: "outbox_events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "processing_lease_expires_at",
                table: "outbox_events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_outbox_processing_lease",
                table: "outbox_events",
                columns: new[] { "status", "processing_lease_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_outbox_processing_lease",
                table: "outbox_events");

            migrationBuilder.DropColumn(
                name: "processing_owner",
                table: "outbox_events");

            migrationBuilder.DropColumn(
                name: "processing_lease_expires_at",
                table: "outbox_events");
        }
    }
}

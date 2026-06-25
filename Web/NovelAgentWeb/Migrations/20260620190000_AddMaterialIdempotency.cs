using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260620190000_AddMaterialIdempotency")]
    public partial class AddMaterialIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "materials",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_materials_idempotency",
                table: "materials",
                columns: new[] { "project_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_materials_idempotency",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "materials");
        }
    }
}

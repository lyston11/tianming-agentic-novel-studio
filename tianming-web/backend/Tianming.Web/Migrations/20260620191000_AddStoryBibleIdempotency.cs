using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260620191000_AddStoryBibleIdempotency")]
    public partial class AddStoryBibleIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "story_constitutions",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "characters",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_story_constitutions_idempotency",
                table: "story_constitutions",
                columns: new[] { "project_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_characters_idempotency",
                table: "characters",
                columns: new[] { "project_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_story_constitutions_idempotency",
                table: "story_constitutions");

            migrationBuilder.DropIndex(
                name: "idx_characters_idempotency",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "story_constitutions");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "characters");
        }
    }
}

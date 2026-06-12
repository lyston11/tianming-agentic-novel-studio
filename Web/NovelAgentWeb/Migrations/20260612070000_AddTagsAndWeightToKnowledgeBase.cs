using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddTagsAndWeightToKnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tags",
                table: "knowledge_base",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "weight",
                table: "knowledge_base",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tags",
                table: "knowledge_base");

            migrationBuilder.DropColumn(
                name: "weight",
                table: "knowledge_base");
        }
    }
}

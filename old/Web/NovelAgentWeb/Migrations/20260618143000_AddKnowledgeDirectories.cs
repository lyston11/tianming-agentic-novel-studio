using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260618143000_AddKnowledgeDirectories")]
    public partial class AddKnowledgeDirectories : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_directories",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    directory_key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_directories", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_directories_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_directories_user_id",
                table: "knowledge_directories",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_directories_user_key",
                table: "knowledge_directories",
                columns: new[] { "user_id", "directory_key" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "knowledge_directories");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres;

public partial class AddBookProductionStateMachine : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "book_plan_json",
            table: "creative_goals",
            type: "jsonb",
            nullable: false,
            defaultValue: "{}");

        migrationBuilder.AddColumn<string>(
            name: "execution_strategy",
            table: "creative_goals",
            type: "text",
            nullable: false,
            defaultValue: "interactive_batch");

        migrationBuilder.CreateTable(
            name: "book_productions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false),
                project_id = table.Column<string>(type: "text", nullable: false),
                goal_id = table.Column<string>(type: "text", nullable: false),
                execution_strategy = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                target_start_chapter_number = table.Column<int>(type: "integer", nullable: false),
                target_end_chapter_number = table.Column<int>(type: "integer", nullable: false),
                next_chapter_number = table.Column<int>(type: "integer", nullable: false),
                batch_size = table.Column<int>(type: "integer", nullable: false),
                current_batch_number = table.Column<int>(type: "integer", nullable: false),
                completion_criteria_json = table.Column<string>(type: "jsonb", nullable: false),
                pause_policy_json = table.Column<string>(type: "jsonb", nullable: false),
                aggregate_version = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_book_productions", x => x.id);
                table.ForeignKey(
                    name: "FK_book_productions_creative_goals_goal_id",
                    column: x => x.goal_id,
                    principalTable: "creative_goals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "production_batches",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false),
                project_id = table.Column<string>(type: "text", nullable: false),
                goal_id = table.Column<string>(type: "text", nullable: false),
                book_production_id = table.Column<string>(type: "text", nullable: false),
                batch_number = table.Column<int>(type: "integer", nullable: false),
                start_chapter_number = table.Column<int>(type: "integer", nullable: false),
                end_chapter_number = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                task_graph_version_id = table.Column<string>(type: "text", nullable: true),
                canon_branch_id = table.Column<string>(type: "text", nullable: true),
                acceptance_actor = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_production_batches", x => x.id);
                table.ForeignKey(
                    name: "FK_production_batches_book_productions_book_production_id",
                    column: x => x.book_production_id,
                    principalTable: "book_productions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_production_batches_creative_goals_goal_id",
                    column: x => x.goal_id,
                    principalTable: "creative_goals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_book_productions_goal_id", "book_productions", "goal_id", unique: true);
        migrationBuilder.CreateIndex("ix_book_productions_status", "book_productions", new[] { "user_id", "project_id", "status", "updated_at" });
        migrationBuilder.CreateIndex("ux_book_productions_goal", "book_productions", new[] { "user_id", "goal_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_production_batches_book_production_id", "production_batches", "book_production_id");
        migrationBuilder.CreateIndex("IX_production_batches_goal_id", "production_batches", "goal_id");
        migrationBuilder.CreateIndex("ix_production_batches_goal_status", "production_batches", new[] { "user_id", "goal_id", "status", "batch_number" });
        migrationBuilder.CreateIndex("ux_production_batches_number", "production_batches", new[] { "user_id", "book_production_id", "batch_number" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("production_batches");
        migrationBuilder.DropTable("book_productions");
        migrationBuilder.DropColumn("book_plan_json", "creative_goals");
        migrationBuilder.DropColumn("execution_strategy", "creative_goals");
    }
}

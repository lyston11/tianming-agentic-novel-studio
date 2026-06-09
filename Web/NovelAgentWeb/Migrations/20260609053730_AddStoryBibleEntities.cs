using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryBibleEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_characters_chapters_first_appearance_chapter_id",
                table: "characters");

            migrationBuilder.DropIndex(
                name: "IX_characters_first_appearance_chapter_id",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "description",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "detail_json_path",
                table: "characters");

            migrationBuilder.RenameColumn(
                name: "rules",
                table: "world_settings",
                newName: "referenced_chapters");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "world_settings",
                newName: "content");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "world_settings",
                newName: "change_log");

            migrationBuilder.RenameColumn(
                name: "identity",
                table: "characters",
                newName: "relationships");

            migrationBuilder.RenameColumn(
                name: "first_appearance_chapter_id",
                table: "characters",
                newName: "background");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "world_settings",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "first_mentioned_chapter",
                table: "world_settings",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_version",
                table: "world_settings",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sub_category",
                table: "world_settings",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "world_settings",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "world_settings",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "user_id",
                table: "world_settings",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "world_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "characters",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "age",
                table: "characters",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "alias",
                table: "characters",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "appearance",
                table: "characters",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "core_goal",
                table: "characters",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "characters",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "current_power_level",
                table: "characters",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_appear_chapter",
                table: "characters",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "characters",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "initial_power_level",
                table: "characters",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_appear_chapter",
                table: "characters",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivation",
                table: "characters",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "special_abilities",
                table: "characters",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "characters",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "characters",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "user_id",
                table: "characters",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "agent_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    run_type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    target_chapter_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "running"),
                    input_params = table.Column<string>(type: "TEXT", nullable: true),
                    output_data = table.Column<string>(type: "TEXT", nullable: true),
                    context_package_size = table.Column<int>(type: "INTEGER", nullable: true),
                    context_package_path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    gate_report_path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    duration_ms = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_runs_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_runs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "foreshadow_ledger",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    planted_in_chapter = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    planted_context = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "planted"),
                    resolved_in_chapter = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    resolved_context = table.Column<string>(type: "TEXT", nullable: true),
                    planted_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    priority = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 5),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_foreshadow_ledger", x => x.id);
                    table.ForeignKey(
                        name: "FK_foreshadow_ledger_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_foreshadow_ledger_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "story_constitutions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    genre = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    sub_genre = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    core_hook = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    reader_promise = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    genre_profile = table.Column<string>(type: "TEXT", nullable: true),
                    target_audience = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    taboos = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_constitutions", x => x.id);
                    table.ForeignKey(
                        name: "FK_story_constitutions_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_story_constitutions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "volume_arcs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    volume_number = table.Column<int>(type: "INTEGER", nullable: false),
                    volume_title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    volume_theme = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    target_chapters = table.Column<int>(type: "INTEGER", nullable: true),
                    current_chapters = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    act1_setup = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: true),
                    act2_confrontation = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: true),
                    act3_climax = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: true),
                    act4_resolution = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: true),
                    key_events = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    major_conflict = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    conflict_escalation = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "planned"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volume_arcs", x => x.id);
                    table.ForeignKey(
                        name: "FK_volume_arcs_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_volume_arcs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_world_settings_category",
                table: "world_settings",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_world_settings_user_id",
                table: "world_settings",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_characters_user_id",
                table: "characters",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_project_id",
                table: "agent_runs",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_run_type",
                table: "agent_runs",
                column: "run_type");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_status",
                table: "agent_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_user_id",
                table: "agent_runs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_foreshadow_ledger_project_id",
                table: "foreshadow_ledger",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_foreshadow_ledger_status",
                table: "foreshadow_ledger",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_foreshadow_ledger_user_id",
                table: "foreshadow_ledger",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_story_constitutions_project_id",
                table: "story_constitutions",
                column: "project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_constitutions_user_id",
                table: "story_constitutions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_volume_arcs_project_id_volume_number",
                table: "volume_arcs",
                columns: new[] { "project_id", "volume_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_volume_arcs_user_id",
                table: "volume_arcs",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_characters_users_user_id",
                table: "characters",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_world_settings_users_user_id",
                table: "world_settings",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_characters_users_user_id",
                table: "characters");

            migrationBuilder.DropForeignKey(
                name: "FK_world_settings_users_user_id",
                table: "world_settings");

            migrationBuilder.DropTable(
                name: "agent_runs");

            migrationBuilder.DropTable(
                name: "foreshadow_ledger");

            migrationBuilder.DropTable(
                name: "story_constitutions");

            migrationBuilder.DropTable(
                name: "volume_arcs");

            migrationBuilder.DropIndex(
                name: "IX_world_settings_category",
                table: "world_settings");

            migrationBuilder.DropIndex(
                name: "IX_world_settings_user_id",
                table: "world_settings");

            migrationBuilder.DropIndex(
                name: "IX_characters_user_id",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "world_settings");

            migrationBuilder.DropColumn(
                name: "first_mentioned_chapter",
                table: "world_settings");

            migrationBuilder.DropColumn(
                name: "previous_version",
                table: "world_settings");

            migrationBuilder.DropColumn(
                name: "sub_category",
                table: "world_settings");

            migrationBuilder.DropColumn(
                name: "title",
                table: "world_settings");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "world_settings");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "world_settings");

            migrationBuilder.DropColumn(
                name: "version",
                table: "world_settings");

            migrationBuilder.DropColumn(
                name: "age",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "alias",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "appearance",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "core_goal",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "current_power_level",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "first_appear_chapter",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "initial_power_level",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "last_appear_chapter",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "motivation",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "special_abilities",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "status",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "characters");

            migrationBuilder.RenameColumn(
                name: "referenced_chapters",
                table: "world_settings",
                newName: "rules");

            migrationBuilder.RenameColumn(
                name: "content",
                table: "world_settings",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "change_log",
                table: "world_settings",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "relationships",
                table: "characters",
                newName: "identity");

            migrationBuilder.RenameColumn(
                name: "background",
                table: "characters",
                newName: "first_appearance_chapter_id");

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "characters",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "characters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "detail_json_path",
                table: "characters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_characters_first_appearance_chapter_id",
                table: "characters",
                column: "first_appearance_chapter_id");

            migrationBuilder.AddForeignKey(
                name: "FK_characters_chapters_first_appearance_chapter_id",
                table: "characters",
                column: "first_appearance_chapter_id",
                principalTable: "chapters",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    username = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false),
                    storage_quota_mb = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 5120),
                    api_call_quota = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 10000),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    last_login_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "novel_projects",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    genre = table.Column<string>(type: "TEXT", nullable: true),
                    sub_genre = table.Column<string>(type: "TEXT", nullable: true),
                    core_hook = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "draft"),
                    word_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    cover_image_url = table.Column<string>(type: "TEXT", nullable: true),
                    storage_project_name = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_novel_projects", x => x.id);
                    table.ForeignKey(
                        name: "FK_novel_projects_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    llm_provider = table.Column<string>(type: "TEXT", nullable: true),
                    llm_api_key_encrypted = table.Column<string>(type: "TEXT", nullable: true),
                    llm_base_url = table.Column<string>(type: "TEXT", nullable: true),
                    llm_model = table.Column<string>(type: "TEXT", nullable: true),
                    llm_temperature = table.Column<float>(type: "REAL", nullable: false, defaultValue: 0.7f),
                    llm_max_tokens = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 4096),
                    embedding_provider = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "local"),
                    embedding_model = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "bge-small-zh-v1.5"),
                    agent_default_risk = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Medium"),
                    agent_auto_continue = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    agent_max_auto_steps = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 12),
                    default_genre = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "玄幻"),
                    default_chapter_word_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3000),
                    theme = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "dark"),
                    language = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "zh-CN")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_settings", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_user_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_memories",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    memory_type = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memories", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memories_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memories_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    session_data = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    title = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "新会话"),
                    is_archived = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_sessions_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_base",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    entry_type = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    usage_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_base", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_base_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "materials",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", nullable: true),
                    content_type = table.Column<string>(type: "TEXT", nullable: true),
                    content = table.Column<string>(type: "TEXT", nullable: true),
                    file_path = table.Column<string>(type: "TEXT", nullable: true),
                    tags = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materials", x => x.id);
                    table.ForeignKey(
                        name: "FK_materials_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_materials_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "volumes",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    volume_number = table.Column<int>(type: "INTEGER", nullable: false),
                    summary = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volumes", x => x.id);
                    table.ForeignKey(
                        name: "FK_volumes_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "world_settings",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    rules = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_world_settings", x => x.id);
                    table.ForeignKey(
                        name: "FK_world_settings_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapters",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    volume_id = table.Column<string>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    chapter_number = table.Column<int>(type: "INTEGER", nullable: false),
                    word_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "draft"),
                    content_path = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapters", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapters_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapters_volumes_volume_id",
                        column: x => x.volume_id,
                        principalTable: "volumes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "characters",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: true),
                    identity = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    personality = table.Column<string>(type: "TEXT", nullable: true),
                    first_appearance_chapter_id = table.Column<string>(type: "TEXT", nullable: true),
                    detail_json_path = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_characters", x => x.id);
                    table.ForeignKey(
                        name: "FK_characters_chapters_first_appearance_chapter_id",
                        column: x => x.first_appearance_chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_characters_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "foreshadows",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "planned"),
                    setup_chapter_id = table.Column<string>(type: "TEXT", nullable: true),
                    payoff_chapter_id = table.Column<string>(type: "TEXT", nullable: true),
                    importance = table.Column<int>(type: "INTEGER", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_foreshadows", x => x.id);
                    table.ForeignKey(
                        name: "FK_foreshadows_chapters_payoff_chapter_id",
                        column: x => x.payoff_chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_foreshadows_chapters_setup_chapter_id",
                        column: x => x.setup_chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_foreshadows_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_memories_user_project",
                table: "agent_memories",
                columns: new[] { "user_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_memories_project_id",
                table: "agent_memories",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_sessions_project_id",
                table: "agent_sessions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_sessions_user_id",
                table: "agent_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_chapters_project",
                table: "chapters",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_chapters_status",
                table: "chapters",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_chapters_volume",
                table: "chapters",
                column: "volume_id");

            migrationBuilder.CreateIndex(
                name: "idx_characters_project",
                table: "characters",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_characters_first_appearance_chapter_id",
                table: "characters",
                column: "first_appearance_chapter_id");

            migrationBuilder.CreateIndex(
                name: "idx_foreshadows_project",
                table: "foreshadows",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_foreshadows_status",
                table: "foreshadows",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_foreshadows_payoff_chapter_id",
                table: "foreshadows",
                column: "payoff_chapter_id");

            migrationBuilder.CreateIndex(
                name: "IX_foreshadows_setup_chapter_id",
                table: "foreshadows",
                column: "setup_chapter_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_base_project_id",
                table: "knowledge_base",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_materials_project",
                table: "materials",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_materials_user",
                table: "materials",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_novel_projects_storage_project_name",
                table: "novel_projects",
                column: "storage_project_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_novel_projects_user_id",
                table: "novel_projects",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_volumes_project_id",
                table: "volumes",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_world_settings_project_id",
                table: "world_settings",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_memories");

            migrationBuilder.DropTable(
                name: "agent_sessions");

            migrationBuilder.DropTable(
                name: "characters");

            migrationBuilder.DropTable(
                name: "foreshadows");

            migrationBuilder.DropTable(
                name: "knowledge_base");

            migrationBuilder.DropTable(
                name: "materials");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "world_settings");

            migrationBuilder.DropTable(
                name: "chapters");

            migrationBuilder.DropTable(
                name: "volumes");

            migrationBuilder.DropTable(
                name: "novel_projects");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}

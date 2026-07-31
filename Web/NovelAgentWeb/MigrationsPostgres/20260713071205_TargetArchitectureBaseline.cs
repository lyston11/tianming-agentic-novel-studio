using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class TargetArchitectureBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_interrupts",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    runtime_run_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    session_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    decision_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_interrupts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_runtime_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    runtime_run_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    session_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    stage = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    artifact_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    artifact_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    display_surface = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "chat"),
                    display_policy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "collapsible"),
                    message = table.Column<string>(type: "text", nullable: false),
                    data_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runtime_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_runtime_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    session_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    locked_project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    executed_tools_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "inspect"),
                    current_phase = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    current_step = table.Column<int>(type: "integer", nullable: false),
                    active_tool = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    user_message = table.Column<string>(type: "text", nullable: false),
                    source_message_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    budget_json = table.Column<string>(type: "text", nullable: false),
                    last_message = table.Column<string>(type: "text", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: false),
                    failure_json = table.Column<string>(type: "text", nullable: false),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runtime_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_tool_executions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    session_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    run_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tool_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    phase = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    risk = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    arguments_json = table.Column<string>(type: "text", nullable: false),
                    arguments_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    side_effects_json = table.Column<string>(type: "text", nullable: false),
                    semantic_contract_json = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    result_phase = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    result_message = table.Column<string>(type: "text", nullable: false),
                    error_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: false),
                    failure_json = table.Column<string>(type: "text", nullable: false),
                    artifact_json = table.Column<string>(type: "text", nullable: false),
                    recommended_next_tool = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    missing_prerequisite = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_tool_executions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_tool_search_snapshots",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    session_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    phase = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    version = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tools_json = table.Column<string>(type: "text", nullable: false),
                    source_execution_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cached_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_tool_search_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "branch_merge_records",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    branch_id = table.Column<string>(type: "text", nullable: false),
                    start_chapter_number = table.Column<int>(type: "integer", nullable: false),
                    end_chapter_number = table.Column<int>(type: "integer", nullable: false),
                    candidate_versions_json = table.Column<string>(type: "jsonb", nullable: false),
                    previous_canon_version = table.Column<string>(type: "text", nullable: false),
                    new_canon_version = table.Column<string>(type: "text", nullable: false),
                    merged_by_user_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_merge_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "candidate_acceptances",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    branch_id = table.Column<string>(type: "text", nullable: false),
                    candidate_chapter_id = table.Column<string>(type: "text", nullable: false),
                    candidate_version = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<string>(type: "text", nullable: false),
                    decided_by_user_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidate_acceptances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "candidate_chapters",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    branch_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: false),
                    chapter_number = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    current_artifact_id = table.Column<string>(type: "text", nullable: false),
                    depends_on_candidate_chapter_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    authorship = table.Column<string>(type: "text", nullable: false),
                    is_protected = table.Column<bool>(type: "boolean", nullable: false),
                    continuity_summary_id = table.Column<string>(type: "text", nullable: true),
                    review_artifact_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidate_chapters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "canon_branches",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    canon_baseline_version = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    start_chapter_number = table.Column<int>(type: "integer", nullable: false),
                    end_chapter_number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    merged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canon_branches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "canon_changes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: false),
                    chapter_version_id = table.Column<string>(type: "text", nullable: false),
                    branch_id = table.Column<string>(type: "text", nullable: true),
                    change_type = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    change_json = table.Column<string>(type: "jsonb", nullable: false),
                    evidence_refs_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canon_changes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chapter_blueprints",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    volume_id = table.Column<string>(type: "text", nullable: true),
                    chapter_id = table.Column<string>(type: "text", nullable: false),
                    chapter_index = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    intent = table.Column<string>(type: "text", nullable: false),
                    key_events_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    characters_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    conflict_note = table.Column<string>(type: "text", nullable: true),
                    ending_note = table.Column<string>(type: "text", nullable: true),
                    required_knowledge_ids_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    applied_design_rule_ids_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    dependency_chapter_ids_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    context_package_id = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "Draft"),
                    target_word_count = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    previous_version_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_blueprints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "continuity_summaries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: false),
                    chapter_version_id = table.Column<string>(type: "text", nullable: false),
                    branch_id = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    summary_json = table.Column<string>(type: "jsonb", nullable: false),
                    evidence_refs_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_continuity_summaries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "creative_goals",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    source_session_id = table.Column<string>(type: "text", nullable: false),
                    goal_type = table.Column<string>(type: "text", nullable: false),
                    collaboration_mode = table.Column<string>(type: "text", nullable: false),
                    human_readable_objective = table.Column<string>(type: "text", nullable: false),
                    target_chapter_range_json = table.Column<string>(type: "jsonb", nullable: false),
                    success_criteria_json = table.Column<string>(type: "jsonb", nullable: false),
                    must_preserve_json = table.Column<string>(type: "jsonb", nullable: false),
                    must_happen_json = table.Column<string>(type: "jsonb", nullable: false),
                    must_not_change_json = table.Column<string>(type: "jsonb", nullable: false),
                    acceptance_policy_json = table.Column<string>(type: "jsonb", nullable: false),
                    rework_policy_json = table.Column<string>(type: "jsonb", nullable: false),
                    total_cost_limit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    reserved_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    actual_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    canon_baseline_version = table.Column<string>(type: "text", nullable: false),
                    knowledge_snapshot_version = table.Column<string>(type: "text", nullable: false),
                    quality_contract_version = table.Column<string>(type: "text", nullable: false),
                    style_profile_version = table.Column<string>(type: "text", nullable: false),
                    model_config_versions_json = table.Column<string>(type: "jsonb", nullable: false),
                    protocol_versions_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_creative_goals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "domain_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: true),
                    branch_id = table.Column<string>(type: "text", nullable: true),
                    aggregate_type = table.Column<string>(type: "text", nullable: false),
                    aggregate_id = table.Column<string>(type: "text", nullable: false),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    artifact_refs_json = table.Column<string>(type: "jsonb", nullable: false),
                    evidence_refs_json = table.Column<string>(type: "jsonb", nullable: false),
                    causation_id = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    model_execution_id = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "goal_context_snapshots",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    canon_version = table.Column<string>(type: "text", nullable: false),
                    knowledge_version = table.Column<string>(type: "text", nullable: false),
                    quality_contract_version = table.Column<string>(type: "text", nullable: false),
                    style_profile_version = table.Column<string>(type: "text", nullable: false),
                    model_config_versions_json = table.Column<string>(type: "jsonb", nullable: false),
                    protocol_versions_json = table.Column<string>(type: "jsonb", nullable: false),
                    content_hashes_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goal_context_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "goal_revisions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    constraint_changes_json = table.Column<string>(type: "jsonb", nullable: false),
                    reusable_artifact_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    invalidated_artifact_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    task_graph_version_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goal_revisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kernel_artifacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: false),
                    branch_id = table.Column<string>(type: "text", nullable: true),
                    artifact_type = table.Column<string>(type: "text", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    content_json = table.Column<string>(type: "jsonb", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    authorship = table.Column<string>(type: "text", nullable: false),
                    is_protected = table.Column<bool>(type: "boolean", nullable: false),
                    model_execution_id = table.Column<string>(type: "text", nullable: true),
                    causation_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kernel_artifacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kernel_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    task_graph_version_id = table.Column<string>(type: "text", nullable: false),
                    branch_id = table.Column<string>(type: "text", nullable: true),
                    kernel_name = table.Column<string>(type: "text", nullable: false),
                    task_type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    dependency_task_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    input_artifact_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    output_artifact_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    lease_owner = table.Column<string>(type: "text", nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kernel_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "model_executions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: false),
                    kernel_name = table.Column<string>(type: "text", nullable: false),
                    model_config_version_id = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    reserved_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    actual_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    provider_request_id = table.Column<string>(type: "text", nullable: true),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    result_content_hash = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_executions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "model_kernel_configurations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    kernel_name = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    base_url = table.Column<string>(type: "text", nullable: true),
                    credential_reference = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    temperature = table.Column<float>(type: "real", nullable: false),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    fallback_json = table.Column<string>(type: "jsonb", nullable: false),
                    custom_instructions = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_kernel_configurations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    runtime_run_id = table.Column<string>(type: "text", nullable: true),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    aggregate_type = table.Column<string>(type: "text", nullable: false),
                    aggregate_id = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processing_owner = table.Column<string>(type: "text", nullable: true),
                    processing_lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_design_rules",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    rule_type = table.Column<string>(type: "text", nullable: false),
                    rule_content = table.Column<string>(type: "text", nullable: false),
                    source_knowledge_ids_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    constraint_level = table.Column<string>(type: "text", nullable: false, defaultValue: "Reference"),
                    scope = table.Column<string>(type: "text", nullable: false, defaultValue: "ProjectWide"),
                    scope_target = table.Column<string>(type: "text", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 50),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "Active"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    previous_version_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_design_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "task_graph_versions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    goal_revision_id = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    graph_json = table.Column<string>(type: "jsonb", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_graph_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    username = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    storage_quota_mb = table.Column<int>(type: "integer", nullable: false, defaultValue: 5120),
                    api_call_quota = table.Column<int>(type: "integer", nullable: false, defaultValue: 10000),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_directories",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    directory_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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

            migrationBuilder.CreateTable(
                name: "novel_projects",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    genre = table.Column<string>(type: "text", nullable: true),
                    sub_genre = table.Column<string>(type: "text", nullable: true),
                    core_hook = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    word_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    cover_image_url = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                    user_id = table.Column<string>(type: "text", nullable: false),
                    llm_provider = table.Column<string>(type: "text", nullable: true),
                    llm_api_key_encrypted = table.Column<string>(type: "text", nullable: true),
                    llm_base_url = table.Column<string>(type: "text", nullable: true),
                    llm_model = table.Column<string>(type: "text", nullable: true),
                    llm_temperature = table.Column<float>(type: "real", nullable: false, defaultValue: 0.7f),
                    llm_max_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 4096),
                    embedding_provider = table.Column<string>(type: "text", nullable: false, defaultValue: "local"),
                    embedding_model = table.Column<string>(type: "text", nullable: false, defaultValue: "bge-small-zh-v1.5"),
                    agent_default_risk = table.Column<string>(type: "text", nullable: false, defaultValue: "Medium"),
                    agent_loop_auto_proceed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    agent_loop_max_steps = table.Column<int>(type: "integer", nullable: false, defaultValue: 12),
                    default_genre = table.Column<string>(type: "text", nullable: false, defaultValue: "玄幻"),
                    default_chapter_word_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 3000),
                    theme = table.Column<string>(type: "text", nullable: false, defaultValue: "dark"),
                    language = table.Column<string>(type: "text", nullable: false, defaultValue: "zh-CN")
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
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    memory_type = table.Column<string>(type: "text", nullable: false),
                    memory_key = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                name: "agent_memory_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    run_id = table.Column<string>(type: "text", nullable: true),
                    source_type = table.Column<string>(type: "text", nullable: false),
                    trigger_type = table.Column<string>(type: "text", nullable: false),
                    memory_scope = table.Column<string>(type: "text", nullable: false),
                    memory_key = table.Column<string>(type: "text", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memory_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memory_events_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memory_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_memory_promotions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    run_id = table.Column<string>(type: "text", nullable: true),
                    source_scope = table.Column<string>(type: "text", nullable: false),
                    target_scope = table.Column<string>(type: "text", nullable: false),
                    source_memory_key = table.Column<string>(type: "text", nullable: false),
                    target_memory_key = table.Column<string>(type: "text", nullable: false),
                    promotion_reason = table.Column<string>(type: "text", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memory_promotions", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memory_promotions_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memory_promotions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_memory_reads",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    run_id = table.Column<string>(type: "text", nullable: true),
                    memory_scope = table.Column<string>(type: "text", nullable: false),
                    memory_keys_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    source_type = table.Column<string>(type: "text", nullable: false, defaultValue: "memory_repository"),
                    consumer = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memory_reads", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memory_reads_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memory_reads_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_memory_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    scope = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memory_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memory_versions_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memory_versions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_reviews",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    runtime_run_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: false),
                    package_id = table.Column<string>(type: "text", nullable: true),
                    review_id = table.Column<string>(type: "text", nullable: false),
                    overall_result = table.Column<string>(type: "text", nullable: false, defaultValue: "Unknown"),
                    validation_overall_result = table.Column<string>(type: "text", nullable: false),
                    requires_rewrite = table.Column<bool>(type: "boolean", nullable: false),
                    quality_score = table.Column<int>(type: "integer", nullable: false),
                    content_length = table.Column<int>(type: "integer", nullable: false),
                    check_count = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    meets_accepted_creative_intents = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    continuity_risk = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    chapter_pacing = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    recommended_action = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    review_json = table.Column<string>(type: "text", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_reviews_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    title = table.Column<string>(type: "text", nullable: false, defaultValue: "新会话"),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    session_data = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                name: "chapter_changes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    runtime_run_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: false),
                    package_id = table.Column<string>(type: "text", nullable: true),
                    changes_json = table.Column<string>(type: "text", nullable: false),
                    canonical_changes_json = table.Column<string>(type: "text", nullable: false),
                    parse_status = table.Column<string>(type: "text", nullable: false, defaultValue: "unknown"),
                    parse_error = table.Column<string>(type: "text", nullable: true),
                    applied_to_fact_snapshot = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_changes_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapter_drafts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    runtime_run_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: false),
                    package_id = table.Column<string>(type: "text", nullable: true),
                    artifact_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "draft_generated"),
                    draft_content = table.Column<string>(type: "text", nullable: false),
                    changes_json = table.Column<string>(type: "text", nullable: true),
                    content_length = table.Column<int>(type: "integer", nullable: false),
                    repair_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    has_changes = table.Column<bool>(type: "boolean", nullable: false),
                    generated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_drafts", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_drafts_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_documents",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    source_type = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    document_role = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    mime_type = table.Column<string>(type: "text", nullable: false, defaultValue: "text/plain"),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_content_documents_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_documents_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "creative_intents",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    runtime_run_id = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false, defaultValue: "chat"),
                    raw_content = table.Column<string>(type: "text", nullable: false),
                    normalized_intent = table.Column<string>(type: "text", nullable: false),
                    target_scope = table.Column<string>(type: "text", nullable: false, defaultValue: "project"),
                    target_volume_id = table.Column<string>(type: "text", nullable: true),
                    target_chapter_id = table.Column<string>(type: "text", nullable: true),
                    target_character_name = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "candidate"),
                    impact_level = table.Column<string>(type: "text", nullable: false, defaultValue: "future_carry"),
                    requires_confirmation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    conflict_status = table.Column<string>(type: "text", nullable: false, defaultValue: "unknown"),
                    decision_reason = table.Column<string>(type: "text", nullable: true),
                    metadata_json = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_creative_intents", x => x.id);
                    table.ForeignKey(
                        name: "FK_creative_intents_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_creative_intents_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "foreshadow_ledger",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    planted_in_chapter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    planted_context = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "planted"),
                    resolved_in_chapter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    resolved_context = table.Column<string>(type: "text", nullable: true),
                    planted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                name: "generation_gate_reports",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    runtime_run_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: false),
                    package_id = table.Column<string>(type: "text", nullable: true),
                    artifact_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    report_json = table.Column<string>(type: "text", nullable: false),
                    protocol_passed = table.Column<bool>(type: "boolean", nullable: false),
                    changes_detected = table.Column<bool>(type: "boolean", nullable: false),
                    fact_snapshot_passed = table.Column<bool>(type: "boolean", nullable: false),
                    blueprint_passed = table.Column<bool>(type: "boolean", nullable: false),
                    rag_passed = table.Column<bool>(type: "boolean", nullable: false),
                    issue_count = table.Column<int>(type: "integer", nullable: false),
                    repair_hint_count = table.Column<int>(type: "integer", nullable: false),
                    validated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generation_gate_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_generation_gate_reports_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_base",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    entry_type = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    vector_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "manual"),
                    source_upload_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    chunk_index = table.Column<int>(type: "integer", nullable: true),
                    extraction_context = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string>(type: "text", nullable: true),
                    weight = table.Column<int>(type: "integer", nullable: false, defaultValue: 5)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_base", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_base_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_knowledge_base_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "production_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    runtime_run_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: true),
                    package_id = table.Column<string>(type: "text", nullable: true),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    stage = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    artifact_type = table.Column<string>(type: "text", nullable: true),
                    artifact_id = table.Column<string>(type: "text", nullable: true),
                    data_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_production_events_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "story_constitutions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    genre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sub_genre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    core_hook = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    reader_promise = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    genre_profile = table.Column<string>(type: "text", nullable: true),
                    target_audience = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    taboos = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                name: "tianming_packages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: true),
                    runtime_run_id = table.Column<string>(type: "text", nullable: true),
                    package_kind = table.Column<string>(type: "text", nullable: false, defaultValue: "chapter_generation"),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    input_json = table.Column<string>(type: "text", nullable: false),
                    dependency_versions_json = table.Column<string>(type: "text", nullable: true),
                    knowledge_snapshot_json = table.Column<string>(type: "text", nullable: true),
                    fact_snapshot_json = table.Column<string>(type: "text", nullable: true),
                    prompt_version = table.Column<string>(type: "text", nullable: true),
                    kernel_version = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tianming_packages", x => x.id);
                    table.ForeignKey(
                        name: "FK_tianming_packages_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "volume_arcs",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    volume_number = table.Column<int>(type: "integer", nullable: false),
                    volume_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    volume_theme = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    target_chapters = table.Column<int>(type: "integer", nullable: true),
                    current_chapters = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    act1_setup = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    act2_confrontation = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    act3_climax = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    act4_resolution = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    key_events = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    major_conflict = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    conflict_escalation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "planned"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "volumes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    volume_number = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true)
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
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sub_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    first_mentioned_chapter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    referenced_chapters = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    previous_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    change_log = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                    table.ForeignKey(
                        name: "FK_world_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_chat_summaries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    start_turn = table.Column<int>(type: "integer", nullable: false),
                    end_turn = table.Column<int>(type: "integer", nullable: false),
                    summary_type = table.Column<string>(type: "text", nullable: false, defaultValue: "summary"),
                    content = table.Column<string>(type: "text", nullable: false),
                    key_decisions_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_chat_summaries", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_chat_summaries_agent_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "agent_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_chat_summaries_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_chat_summaries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_chat_turns",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    turn_index = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    compressed_into_summary_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_chat_turns", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_chat_turns_agent_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "agent_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_chat_turns_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_chat_turns_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    run_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_chapter_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "running"),
                    input_params = table.Column<string>(type: "text", nullable: true),
                    output_data = table.Column<string>(type: "text", nullable: true),
                    output_document_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    context_package_size = table.Column<int>(type: "integer", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_runs_content_documents_output_document_id",
                        column: x => x.output_document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "content_chunks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    document_id = table.Column<string>(type: "text", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    chunk_text = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    char_start = table.Column<int>(type: "integer", nullable: false),
                    char_end = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_chunks", x => x.id);
                    table.UniqueConstraint("AK_content_chunks_id_document_id", x => new { x.id, x.document_id });
                    table.ForeignKey(
                        name: "FK_content_chunks_content_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_processing_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    strategy = table.Column<string>(type: "text", nullable: false, defaultValue: "single_pass"),
                    progress = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_chunks = table.Column<int>(type: "integer", nullable: true),
                    processed_chunks = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    extracted_entries_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    upload_document_id = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_processing_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_processing_tasks_content_documents_upload_documen~",
                        column: x => x.upload_document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_knowledge_processing_tasks_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_processing_tasks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "materials",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    content_type = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string>(type: "text", nullable: true),
                    raw_document_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    vector_chunk_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materials", x => x.id);
                    table.ForeignKey(
                        name: "FK_materials_content_documents_raw_document_id",
                        column: x => x.raw_document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "knowledge_classifications",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    knowledge_id = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    classification_json = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    role = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    scope = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    constraint_level = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    package_policy = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    source_session_id = table.Column<string>(type: "text", nullable: true),
                    source_run_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_classifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_classifications_knowledge_base_knowledge_id",
                        column: x => x.knowledge_id,
                        principalTable: "knowledge_base",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_classifications_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_classifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_conflict_reports",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    knowledge_id = table.Column<string>(type: "text", nullable: false),
                    conflicting_knowledge_ids_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    conflict_type = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    severity = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    impact_scope = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    explanation = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    recommended_action = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    requires_user_decision = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "open"),
                    detection_json = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    source_session_id = table.Column<string>(type: "text", nullable: true),
                    source_run_id = table.Column<string>(type: "text", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "text", nullable: true),
                    resolved_by_session_id = table.Column<string>(type: "text", nullable: true),
                    resolved_by_run_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_conflict_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_conflict_reports_knowledge_base_knowledge_id",
                        column: x => x.knowledge_id,
                        principalTable: "knowledge_base",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_conflict_reports_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_conflict_reports_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_knowledge_usages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    knowledge_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "imported"),
                    source_session_id = table.Column<string>(type: "text", nullable: true),
                    source_run_id = table.Column<string>(type: "text", nullable: true),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false, defaultValue: "Reference"),
                    scope = table.Column<string>(type: "text", nullable: false, defaultValue: "ProjectWide"),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 50),
                    constraint_level = table.Column<string>(type: "text", nullable: false, defaultValue: "Reference"),
                    package_policy = table.Column<string>(type: "text", nullable: false, defaultValue: "RelevantOnly"),
                    bound_version = table.Column<string>(type: "text", nullable: true),
                    used_by_chapters_json = table.Column<string>(type: "text", nullable: true),
                    usage_idempotency_keys_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_knowledge_usages", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_knowledge_usages_knowledge_base_knowledge_id",
                        column: x => x.knowledge_id,
                        principalTable: "knowledge_base",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_knowledge_usages_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_knowledge_usages_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapters",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    volume_id = table.Column<string>(type: "text", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    chapter_number = table.Column<int>(type: "integer", nullable: false),
                    word_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    current_document_id = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapters", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapters_content_documents_current_document_id",
                        column: x => x.current_document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "content_vector_points",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    document_id = table.Column<string>(type: "text", nullable: false),
                    chunk_id = table.Column<string>(type: "text", nullable: true),
                    qdrant_collection = table.Column<string>(type: "text", nullable: false),
                    qdrant_point_id = table.Column<string>(type: "text", nullable: false),
                    vector_model = table.Column<string>(type: "text", nullable: false),
                    indexed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    index_status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_vector_points", x => x.id);
                    table.ForeignKey(
                        name: "FK_content_vector_points_content_chunks_chunk_id_document_id",
                        columns: x => new { x.chunk_id, x.document_id },
                        principalTable: "content_chunks",
                        principalColumns: new[] { "id", "document_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_vector_points_content_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "revision_plans",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    creative_intent_id = table.Column<string>(type: "text", nullable: true),
                    knowledge_conflict_report_id = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    runtime_run_id = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false, defaultValue: "creative_intent"),
                    plan_type = table.Column<string>(type: "text", nullable: false, defaultValue: "future_carry"),
                    target_scope = table.Column<string>(type: "text", nullable: false, defaultValue: "project"),
                    target_volume_id = table.Column<string>(type: "text", nullable: true),
                    target_chapter_id = table.Column<string>(type: "text", nullable: true),
                    target_chapter_logical_id = table.Column<string>(type: "text", nullable: true),
                    target_chapter_display_name = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    requirements_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    continuity_requirements_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    impact_analysis_json = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    affected_chapter_ids_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    invalidated_package_ids_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    risk_level = table.Column<string>(type: "text", nullable: false, defaultValue: "medium"),
                    recommendation = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revision_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_revision_plans_creative_intents_creative_intent_id",
                        column: x => x.creative_intent_id,
                        principalTable: "creative_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_revision_plans_knowledge_conflict_reports_knowledge_conflic~",
                        column: x => x.knowledge_conflict_report_id,
                        principalTable: "knowledge_conflict_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_revision_plans_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_revision_plans_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapter_versions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: false),
                    content_document_id = table.Column<string>(type: "text", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    word_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    runtime_run_id = table.Column<string>(type: "text", nullable: true),
                    package_id = table.Column<string>(type: "text", nullable: true),
                    gate_report_json = table.Column<string>(type: "text", nullable: true),
                    agent_review_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_versions_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapter_versions_content_documents_content_document_id",
                        column: x => x.content_document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapter_versions_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "characters",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    project_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    alias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    age = table.Column<int>(type: "integer", nullable: true),
                    gender = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    appearance = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    personality = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    background = table.Column<string>(type: "text", nullable: true),
                    initial_power_level = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    current_power_level = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    special_abilities = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    core_goal = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    motivation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    relationships = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "active"),
                    first_appear_chapter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    last_appear_chapter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_characters", x => x.id);
                    table.ForeignKey(
                        name: "FK_characters_chapters_first_appear_chapter",
                        column: x => x.first_appear_chapter,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_characters_chapters_last_appear_chapter",
                        column: x => x.last_appear_chapter,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_characters_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_characters_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "foreshadows",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "planned"),
                    setup_chapter_id = table.Column<string>(type: "text", nullable: true),
                    payoff_chapter_id = table.Column<string>(type: "text", nullable: true),
                    importance = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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

            migrationBuilder.CreateTable(
                name: "project_fact_snapshots",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    chapter_id = table.Column<string>(type: "text", nullable: true),
                    chapter_version_id = table.Column<string>(type: "text", nullable: true),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false, defaultValue: "unknown"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_fact_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_fact_snapshots_chapter_versions_chapter_version_id",
                        column: x => x.chapter_version_id,
                        principalTable: "chapter_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_project_fact_snapshots_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_project_fact_snapshots_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_summaries_project_id",
                table: "agent_chat_summaries",
                column: "project_id");

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

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_summaries_session_id",
                table: "agent_chat_summaries",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_summaries_user_id",
                table: "agent_chat_summaries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_turns_project_id",
                table: "agent_chat_turns",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_turns_session_id_turn_index",
                table: "agent_chat_turns",
                columns: new[] { "session_id", "turn_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_turns_user_id",
                table: "agent_chat_turns",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_interrupts_run_pending",
                table: "agent_interrupts",
                columns: new[] { "runtime_run_id", "status", "priority", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_interrupts_session_pending",
                table: "agent_interrupts",
                columns: new[] { "user_id", "session_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_memories_user_project",
                table: "agent_memories",
                columns: new[] { "user_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_memories_project_id",
                table: "agent_memories",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memories_user_project_type",
                table: "agent_memories",
                columns: new[] { "user_id", "project_id", "memory_type" },
                unique: true,
                filter: "project_id IS NOT NULL AND session_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memories_user_session_type",
                table: "agent_memories",
                columns: new[] { "user_id", "session_id", "memory_type" },
                unique: true,
                filter: "session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memories_user_type_global",
                table: "agent_memories",
                columns: new[] { "user_id", "memory_type" },
                unique: true,
                filter: "project_id IS NULL AND session_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_events_project_id",
                table: "agent_memory_events",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_events_run_id",
                table: "agent_memory_events",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_events_session_id",
                table: "agent_memory_events",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_events_user_id",
                table: "agent_memory_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_promotions_project_created",
                table: "agent_memory_promotions",
                columns: new[] { "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_promotions_run",
                table: "agent_memory_promotions",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_promotions_session",
                table: "agent_memory_promotions",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_promotions_user_created",
                table: "agent_memory_promotions",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_reads_project_created",
                table: "agent_memory_reads",
                columns: new[] { "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_reads_run",
                table: "agent_memory_reads",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_reads_session",
                table: "agent_memory_reads",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_reads_user_created",
                table: "agent_memory_reads",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_project_id",
                table: "agent_memory_versions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_user_project_scope",
                table: "agent_memory_versions",
                columns: new[] { "user_id", "project_id", "scope" },
                unique: true,
                filter: "project_id IS NOT NULL AND session_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_user_project_session_scope_not_null",
                table: "agent_memory_versions",
                columns: new[] { "user_id", "project_id", "session_id", "scope" },
                unique: true,
                filter: "project_id IS NOT NULL AND session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_user_scope_global",
                table: "agent_memory_versions",
                columns: new[] { "user_id", "scope" },
                unique: true,
                filter: "project_id IS NULL AND session_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_user_session_scope",
                table: "agent_memory_versions",
                columns: new[] { "user_id", "session_id", "scope" },
                unique: true,
                filter: "project_id IS NULL AND session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_agent_reviews_project_chapter_created",
                table: "agent_reviews",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_reviews_project_result",
                table: "agent_reviews",
                columns: new[] { "project_id", "overall_result" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_reviews_run_created",
                table: "agent_reviews",
                columns: new[] { "runtime_run_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_output_document_id",
                table: "agent_runs",
                column: "output_document_id");

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
                name: "idx_agent_runtime_events_run_recent",
                table: "agent_runtime_events",
                columns: new[] { "runtime_run_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_runtime_events_session_recent",
                table: "agent_runtime_events",
                columns: new[] { "user_id", "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_runtime_runs_project_status",
                table: "agent_runtime_runs",
                columns: new[] { "user_id", "project_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_runtime_runs_session_status",
                table: "agent_runtime_runs",
                columns: new[] { "user_id", "session_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_agent_runtime_runs_active_session",
                table: "agent_runtime_runs",
                columns: new[] { "user_id", "session_id" },
                unique: true,
                filter: "status IN ('queued', 'running')");

            migrationBuilder.CreateIndex(
                name: "ux_agent_runtime_runs_idempotency",
                table: "agent_runtime_runs",
                columns: new[] { "user_id", "session_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key <> ''");

            migrationBuilder.CreateIndex(
                name: "idx_agent_sessions_idempotency",
                table: "agent_sessions",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_sessions_project_id",
                table: "agent_sessions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_tool_executions_dedupe",
                table: "agent_tool_executions",
                columns: new[] { "user_id", "project_id", "tool_name", "arguments_hash" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_tool_executions_scope_recent",
                table: "agent_tool_executions",
                columns: new[] { "user_id", "project_id", "session_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_tool_search_snapshots_expires_at",
                table: "agent_tool_search_snapshots",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "idx_agent_tool_search_snapshots_scope_version",
                table: "agent_tool_search_snapshots",
                columns: new[] { "user_id", "project_id", "session_id", "phase", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_merge_records_branch",
                table: "branch_merge_records",
                columns: new[] { "user_id", "branch_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_candidate_acceptances_chapter_version",
                table: "candidate_acceptances",
                columns: new[] { "user_id", "candidate_chapter_id", "candidate_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_candidate_chapters_branch_status",
                table: "candidate_chapters",
                columns: new[] { "user_id", "branch_id", "status", "chapter_number" });

            migrationBuilder.CreateIndex(
                name: "ux_candidate_chapters_branch_number_version",
                table: "candidate_chapters",
                columns: new[] { "user_id", "branch_id", "chapter_number", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_canon_branches_project_status",
                table: "canon_branches",
                columns: new[] { "user_id", "project_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_canon_branches_active_goal",
                table: "canon_branches",
                columns: new[] { "user_id", "goal_id" },
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_canon_changes_project_type",
                table: "canon_changes",
                columns: new[] { "user_id", "project_id", "change_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_blueprints_chapter_index",
                table: "chapter_blueprints",
                columns: new[] { "project_id", "chapter_index" });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_blueprints_project_chapter_status",
                table: "chapter_blueprints",
                columns: new[] { "project_id", "chapter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_blueprints_user_project_created",
                table: "chapter_blueprints",
                columns: new[] { "user_id", "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_blueprints_version",
                table: "chapter_blueprints",
                columns: new[] { "project_id", "chapter_id", "version" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_changes_project_chapter_created",
                table: "chapter_changes",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_changes_project_parse_status",
                table: "chapter_changes",
                columns: new[] { "project_id", "parse_status" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_changes_run_created",
                table: "chapter_changes",
                columns: new[] { "runtime_run_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_drafts_project_chapter_created",
                table: "chapter_drafts",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_drafts_project_status",
                table: "chapter_drafts",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_drafts_run_created",
                table: "chapter_drafts",
                columns: new[] { "runtime_run_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_versions_chapter_version",
                table: "chapter_versions",
                columns: new[] { "chapter_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_chapter_versions_document",
                table: "chapter_versions",
                column: "content_document_id");

            migrationBuilder.CreateIndex(
                name: "idx_chapter_versions_project_chapter_created",
                table: "chapter_versions",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_chapters_current_document",
                table: "chapters",
                column: "current_document_id");

            migrationBuilder.CreateIndex(
                name: "idx_chapters_idempotency",
                table: "chapters",
                columns: new[] { "project_id", "idempotency_key" },
                unique: true);

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
                name: "idx_characters_idempotency",
                table: "characters",
                columns: new[] { "project_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_characters_project",
                table: "characters",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_characters_first_appear_chapter",
                table: "characters",
                column: "first_appear_chapter");

            migrationBuilder.CreateIndex(
                name: "IX_characters_last_appear_chapter",
                table: "characters",
                column: "last_appear_chapter");

            migrationBuilder.CreateIndex(
                name: "IX_characters_user_id",
                table: "characters",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_chunks_document_id_chunk_index",
                table: "content_chunks",
                columns: new[] { "document_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_documents_project_id",
                table: "content_documents",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_documents_source_type_source_id_version",
                table: "content_documents",
                columns: new[] { "source_type", "source_id", "version" });

            migrationBuilder.CreateIndex(
                name: "IX_content_documents_user_id",
                table: "content_documents",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_vector_points_chunk_id",
                table: "content_vector_points",
                column: "chunk_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_vector_points_chunk_id_document_id",
                table: "content_vector_points",
                columns: new[] { "chunk_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_content_vector_points_document_id",
                table: "content_vector_points",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_vector_points_qdrant_collection_qdrant_point_id",
                table: "content_vector_points",
                columns: new[] { "qdrant_collection", "qdrant_point_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_continuity_summaries_chapter_version",
                table: "continuity_summaries",
                columns: new[] { "user_id", "chapter_version_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_creative_goals_scope_status",
                table: "creative_goals",
                columns: new[] { "user_id", "project_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_creative_goals_scope_idempotency",
                table: "creative_goals",
                columns: new[] { "user_id", "project_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key <> ''");

            migrationBuilder.CreateIndex(
                name: "idx_creative_intents_idempotency",
                table: "creative_intents",
                columns: new[] { "user_id", "project_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_creative_intents_project_chapter_status",
                table: "creative_intents",
                columns: new[] { "project_id", "target_chapter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_creative_intents_project_status_created",
                table: "creative_intents",
                columns: new[] { "project_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_creative_intents_session_created",
                table: "creative_intents",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_domain_events_aggregate_version",
                table: "domain_events",
                columns: new[] { "user_id", "aggregate_type", "aggregate_id", "aggregate_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_domain_events_idempotency",
                table: "domain_events",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

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
                name: "idx_generation_gate_reports_project_chapter_created",
                table: "generation_gate_reports",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_generation_gate_reports_project_status",
                table: "generation_gate_reports",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_generation_gate_reports_run_created",
                table: "generation_gate_reports",
                columns: new[] { "runtime_run_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_goal_context_snapshots_goal",
                table: "goal_context_snapshots",
                columns: new[] { "user_id", "goal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_goal_revisions_goal_number",
                table: "goal_revisions",
                columns: new[] { "user_id", "goal_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kernel_artifacts_content",
                table: "kernel_artifacts",
                columns: new[] { "user_id", "goal_id", "content_hash", "artifact_type", "schema_version" });

            migrationBuilder.CreateIndex(
                name: "ix_kernel_artifacts_task",
                table: "kernel_artifacts",
                columns: new[] { "user_id", "task_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_kernel_tasks_claim",
                table: "kernel_tasks",
                columns: new[] { "status", "priority", "lease_expires_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_kernel_tasks_goal_status",
                table: "kernel_tasks",
                columns: new[] { "user_id", "goal_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_kernel_tasks_idempotency",
                table: "kernel_tasks",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_base_idempotency",
                table: "knowledge_base",
                columns: new[] { "user_id", "project_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_base_source_project",
                table: "knowledge_base",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_base_user",
                table: "knowledge_base",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_base_user_archived",
                table: "knowledge_base",
                columns: new[] { "user_id", "is_archived" });

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_classifications_project_knowledge",
                table: "knowledge_classifications",
                columns: new[] { "user_id", "project_id", "knowledge_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_classifications_knowledge_id",
                table: "knowledge_classifications",
                column: "knowledge_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_classifications_project_id",
                table: "knowledge_classifications",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_conflict_reports_knowledge",
                table: "knowledge_conflict_reports",
                columns: new[] { "project_id", "knowledge_id" });

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_conflict_reports_project_status",
                table: "knowledge_conflict_reports",
                columns: new[] { "user_id", "project_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_conflict_reports_knowledge_id",
                table: "knowledge_conflict_reports",
                column: "knowledge_id");

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_directories_idempotency",
                table: "knowledge_directories",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_directories_user_key",
                table: "knowledge_directories",
                columns: new[] { "user_id", "directory_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_processing_tasks_idempotency",
                table: "knowledge_processing_tasks",
                columns: new[] { "user_id", "project_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_processing_tasks_project_id",
                table: "knowledge_processing_tasks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_processing_tasks_status",
                table: "knowledge_processing_tasks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_processing_tasks_upload_document_id",
                table: "knowledge_processing_tasks",
                column: "upload_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_processing_tasks_user_id",
                table: "knowledge_processing_tasks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_materials_idempotency",
                table: "materials",
                columns: new[] { "project_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_materials_project",
                table: "materials",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_materials_raw_document",
                table: "materials",
                column: "raw_document_id");

            migrationBuilder.CreateIndex(
                name: "idx_materials_user",
                table: "materials",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_model_executions_goal_status",
                table: "model_executions",
                columns: new[] { "user_id", "goal_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_model_executions_provider_request",
                table: "model_executions",
                columns: new[] { "provider", "provider_request_id" });

            migrationBuilder.CreateIndex(
                name: "ux_model_kernel_configurations_active",
                table: "model_kernel_configurations",
                columns: new[] { "user_id", "project_id", "kernel_name" },
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ux_model_kernel_configurations_version",
                table: "model_kernel_configurations",
                columns: new[] { "user_id", "project_id", "kernel_name", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_novel_projects_idempotency",
                table: "novel_projects",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_outbox_aggregate",
                table: "outbox_events",
                columns: new[] { "aggregate_type", "aggregate_id" });

            migrationBuilder.CreateIndex(
                name: "idx_outbox_processing_lease",
                table: "outbox_events",
                columns: new[] { "status", "processing_lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "idx_outbox_status_retry",
                table: "outbox_events",
                columns: new[] { "status", "next_attempt_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_outbox_idempotency",
                table: "outbox_events",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key <> ''");

            migrationBuilder.CreateIndex(
                name: "idx_production_events_project_chapter_created",
                table: "production_events",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_production_events_run_created",
                table: "production_events",
                columns: new[] { "runtime_run_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_project_design_rules_project_rule_type_status",
                table: "project_design_rules",
                columns: new[] { "project_id", "rule_type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_project_design_rules_user_project_created",
                table: "project_design_rules",
                columns: new[] { "user_id", "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_project_design_rules_version",
                table: "project_design_rules",
                columns: new[] { "project_id", "rule_type", "version" });

            migrationBuilder.CreateIndex(
                name: "idx_fact_snapshots_project_chapter_version",
                table: "project_fact_snapshots",
                columns: new[] { "project_id", "chapter_id", "version_number" });

            migrationBuilder.CreateIndex(
                name: "idx_fact_snapshots_project_created",
                table: "project_fact_snapshots",
                columns: new[] { "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_project_fact_snapshots_chapter_id",
                table: "project_fact_snapshots",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_fact_snapshots_chapter_version_id",
                table: "project_fact_snapshots",
                column: "chapter_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_knowledge_usages_knowledge_id",
                table: "project_knowledge_usages",
                column: "knowledge_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_knowledge_usages_project_id",
                table: "project_knowledge_usages",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_knowledge_usages_user_id_project_id_knowledge_id",
                table: "project_knowledge_usages",
                columns: new[] { "user_id", "project_id", "knowledge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_revision_plans_conflict_report",
                table: "revision_plans",
                column: "knowledge_conflict_report_id");

            migrationBuilder.CreateIndex(
                name: "idx_revision_plans_creative_intent",
                table: "revision_plans",
                column: "creative_intent_id");

            migrationBuilder.CreateIndex(
                name: "idx_revision_plans_idempotency",
                table: "revision_plans",
                columns: new[] { "user_id", "project_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_revision_plans_project_chapter_status",
                table: "revision_plans",
                columns: new[] { "project_id", "target_chapter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_revision_plans_project_status_created",
                table: "revision_plans",
                columns: new[] { "project_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_story_constitutions_idempotency",
                table: "story_constitutions",
                columns: new[] { "project_id", "idempotency_key" },
                unique: true);

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
                name: "ux_task_graph_versions_goal_version",
                table: "task_graph_versions",
                columns: new[] { "user_id", "goal_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_tianming_packages_project_chapter",
                table: "tianming_packages",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_tianming_packages_run_created",
                table: "tianming_packages",
                columns: new[] { "runtime_run_id", "created_at" });

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
                name: "idx_volume_arcs_idempotency",
                table: "volume_arcs",
                columns: new[] { "project_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_volume_arcs_project_id_volume_number",
                table: "volume_arcs",
                columns: new[] { "project_id", "volume_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_volume_arcs_user_id",
                table: "volume_arcs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_volumes_project_id",
                table: "volumes",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_world_settings_category",
                table: "world_settings",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_world_settings_project_id",
                table: "world_settings",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_world_settings_user_id",
                table: "world_settings",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_chat_summaries");

            migrationBuilder.DropTable(
                name: "agent_chat_turns");

            migrationBuilder.DropTable(
                name: "agent_interrupts");

            migrationBuilder.DropTable(
                name: "agent_memories");

            migrationBuilder.DropTable(
                name: "agent_memory_events");

            migrationBuilder.DropTable(
                name: "agent_memory_promotions");

            migrationBuilder.DropTable(
                name: "agent_memory_reads");

            migrationBuilder.DropTable(
                name: "agent_memory_versions");

            migrationBuilder.DropTable(
                name: "agent_reviews");

            migrationBuilder.DropTable(
                name: "agent_runs");

            migrationBuilder.DropTable(
                name: "agent_runtime_events");

            migrationBuilder.DropTable(
                name: "agent_runtime_runs");

            migrationBuilder.DropTable(
                name: "agent_tool_executions");

            migrationBuilder.DropTable(
                name: "agent_tool_search_snapshots");

            migrationBuilder.DropTable(
                name: "branch_merge_records");

            migrationBuilder.DropTable(
                name: "candidate_acceptances");

            migrationBuilder.DropTable(
                name: "candidate_chapters");

            migrationBuilder.DropTable(
                name: "canon_branches");

            migrationBuilder.DropTable(
                name: "canon_changes");

            migrationBuilder.DropTable(
                name: "chapter_blueprints");

            migrationBuilder.DropTable(
                name: "chapter_changes");

            migrationBuilder.DropTable(
                name: "chapter_drafts");

            migrationBuilder.DropTable(
                name: "characters");

            migrationBuilder.DropTable(
                name: "content_vector_points");

            migrationBuilder.DropTable(
                name: "continuity_summaries");

            migrationBuilder.DropTable(
                name: "creative_goals");

            migrationBuilder.DropTable(
                name: "domain_events");

            migrationBuilder.DropTable(
                name: "foreshadow_ledger");

            migrationBuilder.DropTable(
                name: "foreshadows");

            migrationBuilder.DropTable(
                name: "generation_gate_reports");

            migrationBuilder.DropTable(
                name: "goal_context_snapshots");

            migrationBuilder.DropTable(
                name: "goal_revisions");

            migrationBuilder.DropTable(
                name: "kernel_artifacts");

            migrationBuilder.DropTable(
                name: "kernel_tasks");

            migrationBuilder.DropTable(
                name: "knowledge_classifications");

            migrationBuilder.DropTable(
                name: "knowledge_directories");

            migrationBuilder.DropTable(
                name: "knowledge_processing_tasks");

            migrationBuilder.DropTable(
                name: "materials");

            migrationBuilder.DropTable(
                name: "model_executions");

            migrationBuilder.DropTable(
                name: "model_kernel_configurations");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropTable(
                name: "production_events");

            migrationBuilder.DropTable(
                name: "project_design_rules");

            migrationBuilder.DropTable(
                name: "project_fact_snapshots");

            migrationBuilder.DropTable(
                name: "project_knowledge_usages");

            migrationBuilder.DropTable(
                name: "revision_plans");

            migrationBuilder.DropTable(
                name: "story_constitutions");

            migrationBuilder.DropTable(
                name: "task_graph_versions");

            migrationBuilder.DropTable(
                name: "tianming_packages");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "volume_arcs");

            migrationBuilder.DropTable(
                name: "world_settings");

            migrationBuilder.DropTable(
                name: "agent_sessions");

            migrationBuilder.DropTable(
                name: "content_chunks");

            migrationBuilder.DropTable(
                name: "chapter_versions");

            migrationBuilder.DropTable(
                name: "creative_intents");

            migrationBuilder.DropTable(
                name: "knowledge_conflict_reports");

            migrationBuilder.DropTable(
                name: "chapters");

            migrationBuilder.DropTable(
                name: "knowledge_base");

            migrationBuilder.DropTable(
                name: "content_documents");

            migrationBuilder.DropTable(
                name: "volumes");

            migrationBuilder.DropTable(
                name: "novel_projects");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}

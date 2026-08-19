using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tianming.NovelAgent.Infrastructure.Persistence;

#nullable disable

namespace Tianming.NovelAgent.Infrastructure.Migrations;

[DbContext(typeof(AgentControlDbContext))]
[Migration("202608170001_InitializeNovelAgentControlPlane")]
public sealed class InitializeNovelAgentControlPlane : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS agent_conversation_messages (
                id text PRIMARY KEY,
                user_id text NOT NULL,
                project_id text NOT NULL,
                session_id text NOT NULL,
                role text NOT NULL,
                content text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_agent_conversation_messages_scope
                ON agent_conversation_messages (user_id, session_id, created_at);

            CREATE TABLE IF NOT EXISTS agent_conversation_turns (
                id text PRIMARY KEY,
                user_id text NOT NULL,
                project_id text NOT NULL,
                session_id text NOT NULL,
                idempotency_key text NOT NULL,
                result_json jsonb NOT NULL,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_agent_conversation_turns_idempotency
                ON agent_conversation_turns (user_id, session_id, idempotency_key);

            CREATE TABLE IF NOT EXISTS agent_conversation_runtime_checkpoints (
                id text PRIMARY KEY,
                user_id text NOT NULL,
                project_id text NOT NULL,
                session_id text NOT NULL,
                runtime text NOT NULL,
                checkpoint_json jsonb NOT NULL,
                version bigint NOT NULL DEFAULT 1,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_agent_conversation_runtime_checkpoints_scope
                ON agent_conversation_runtime_checkpoints (user_id, session_id, runtime);

            CREATE TABLE IF NOT EXISTS agent_goal_proposals (
                id text PRIMARY KEY,
                user_id text NOT NULL,
                project_id text NOT NULL,
                source_session_id text NOT NULL,
                contract_json jsonb NOT NULL,
                contract_hash text NOT NULL,
                status text NOT NULL,
                decision_reason text NULL,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_agent_goal_proposals_scope
                ON agent_goal_proposals (user_id, project_id, status, created_at);

            CREATE TABLE IF NOT EXISTS agent_context_checkpoints (
                id text PRIMARY KEY,
                user_id text NOT NULL,
                project_id text NOT NULL,
                contract_hash text NOT NULL,
                canon_baseline_version text NOT NULL,
                knowledge_snapshot_version text NOT NULL,
                style_profile_version text NOT NULL,
                model_versions_json jsonb NOT NULL,
                prompt_versions_json jsonb NOT NULL,
                schema_versions_json jsonb NOT NULL,
                protocol_versions_json jsonb NOT NULL,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_agent_context_checkpoints_contract
                ON agent_context_checkpoints (user_id, project_id, contract_hash);

            CREATE TABLE IF NOT EXISTS agent_stream_events (
                id text PRIMARY KEY,
                user_id text NOT NULL,
                project_id text NOT NULL,
                stream_kind text NOT NULL,
                stream_id text NOT NULL,
                sequence bigint NOT NULL,
                event_type text NOT NULL,
                schema_version integer NOT NULL,
                correlation_id text NOT NULL,
                causation_id text NULL,
                session_id text NULL,
                goal_id text NULL,
                production_id text NULL,
                payload_json jsonb NOT NULL,
                occurred_at timestamptz NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_agent_stream_events_sequence
                ON agent_stream_events (user_id, stream_kind, stream_id, sequence);
            CREATE INDEX IF NOT EXISTS ix_agent_stream_events_project
                ON agent_stream_events (user_id, project_id, occurred_at);

            CREATE TABLE IF NOT EXISTS agent_stream_sequences (
                id text PRIMARY KEY,
                user_id text NOT NULL,
                stream_kind text NOT NULL,
                stream_id text NOT NULL,
                next_sequence bigint NOT NULL DEFAULT 1,
                version bigint NOT NULL DEFAULT 1
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_agent_stream_sequences_scope
                ON agent_stream_sequences (user_id, stream_kind, stream_id);

            CREATE TABLE IF NOT EXISTS agent_canon_write_leases (
                id text PRIMARY KEY,
                user_id text NOT NULL,
                project_id text NOT NULL,
                production_id text NOT NULL,
                owner text NOT NULL,
                expires_at timestamptz NOT NULL,
                fence_token bigint NOT NULL,
                version bigint NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_agent_canon_write_leases_project
                ON agent_canon_write_leases (user_id, project_id);
            CREATE INDEX IF NOT EXISTS ix_agent_canon_write_leases_expiry
                ON agent_canon_write_leases (expires_at, production_id);

            ALTER TABLE creative_goals ADD COLUMN IF NOT EXISTS current_revision_id text NULL;

            ALTER TABLE goal_revisions ADD COLUMN IF NOT EXISTS contract_json jsonb NOT NULL DEFAULT '{}'::jsonb;
            ALTER TABLE goal_revisions ADD COLUMN IF NOT EXISTS context_json jsonb NOT NULL DEFAULT '{}'::jsonb;
            ALTER TABLE goal_revisions ADD COLUMN IF NOT EXISTS contract_hash text NOT NULL DEFAULT '';
            ALTER TABLE goal_revisions ADD COLUMN IF NOT EXISTS schema_version text NOT NULL DEFAULT '1';
            ALTER TABLE goal_revisions ADD COLUMN IF NOT EXISTS confirmation_actor_id text NOT NULL DEFAULT '';
            ALTER TABLE goal_revisions ADD COLUMN IF NOT EXISTS confirmation_time timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE goal_revisions ADD COLUMN IF NOT EXISTS confirmation_idempotency_key text NOT NULL DEFAULT '';
            ALTER TABLE goal_revisions ADD COLUMN IF NOT EXISTS source_session_id text NOT NULL DEFAULT '';
            ALTER TABLE goal_revisions ADD COLUMN IF NOT EXISTS source_proposal_id text NOT NULL DEFAULT '';
            ALTER TABLE goal_revisions ADD COLUMN IF NOT EXISTS previous_revision_id text NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_goal_revisions_confirmation_idempotency
                ON goal_revisions (user_id, project_id, confirmation_idempotency_key)
                WHERE confirmation_idempotency_key <> '';

            ALTER TABLE book_productions ADD COLUMN IF NOT EXISTS goal_revision_id text NOT NULL DEFAULT '';
            ALTER TABLE book_productions ADD COLUMN IF NOT EXISTS task_graph_version_id text NOT NULL DEFAULT '';
            ALTER TABLE book_productions ADD COLUMN IF NOT EXISTS canon_lease_owner text NULL;
            ALTER TABLE book_productions ADD COLUMN IF NOT EXISTS canon_lease_expires_at timestamptz NULL;
            ALTER TABLE book_productions ADD COLUMN IF NOT EXISTS canon_lease_fence_token bigint NULL;
            ALTER TABLE book_productions ADD COLUMN IF NOT EXISTS terminal_reason text NULL;

            ALTER TABLE outbox_events ADD COLUMN IF NOT EXISTS stream_event_id text NULL;
            """);

        migrationBuilder.Sql("""
            DO $rls$
            DECLARE table_name text;
            BEGIN
                FOREACH table_name IN ARRAY ARRAY[
                    'agent_conversation_messages',
                    'agent_conversation_turns',
                    'agent_conversation_runtime_checkpoints',
                    'agent_goal_proposals',
                    'agent_context_checkpoints',
                    'agent_stream_events',
                    'agent_stream_sequences',
                    'agent_canon_write_leases'
                ]
                LOOP
                    EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', table_name);
                    EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', table_name);
                    EXECUTE format('DROP POLICY IF EXISTS agent_user_scope ON %I', table_name);
                    EXECUTE format(
                        'CREATE POLICY agent_user_scope ON %I USING (user_id = NULLIF(current_setting(''app.current_user_id'', true), '''')) WITH CHECK (user_id = NULLIF(current_setting(''app.current_user_id'', true), ''''))',
                        table_name);
                END LOOP;
            END $rls$;

            DO $grants$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'novelagent_app') THEN
                    GRANT SELECT, INSERT, UPDATE, DELETE ON
                        agent_conversation_messages,
                        agent_conversation_turns,
                        agent_conversation_runtime_checkpoints,
                        agent_goal_proposals,
                        agent_context_checkpoints,
                        agent_stream_events,
                        agent_stream_sequences,
                        agent_canon_write_leases
                    TO novelagent_app;
                END IF;
            END $grants$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS agent_canon_write_leases;
            DROP TABLE IF EXISTS agent_stream_sequences;
            DROP TABLE IF EXISTS agent_stream_events;
            DROP TABLE IF EXISTS agent_context_checkpoints;
            DROP TABLE IF EXISTS agent_goal_proposals;
            DROP TABLE IF EXISTS agent_conversation_runtime_checkpoints;
            DROP TABLE IF EXISTS agent_conversation_turns;
            DROP TABLE IF EXISTS agent_conversation_messages;
            """);
    }
}

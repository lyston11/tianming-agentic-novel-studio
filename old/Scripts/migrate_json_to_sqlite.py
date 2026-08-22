#!/usr/bin/env python3
"""Migrate all JSON-based business data into SQLite.

Reads from App_Data/Projects/ (JSON files) and writes to App_Data/Database/novelagent.db.
Idempotent: skips rows that already exist (by primary key).
"""

import json
import os
import sqlite3
import uuid
from datetime import datetime, timezone

DB_PATH = os.path.join(os.path.dirname(__file__), '..', 'Web', 'NovelAgentWeb', 'App_Data', 'Database', 'novelagent.db')
PROJECTS_BASE = os.path.join(os.path.dirname(__file__), '..', 'Web', 'NovelAgentWeb', 'App_Data', 'Projects')
SETTINGS_PATH = os.path.join(PROJECTS_BASE, 'AgenticNovelStudio', 'Settings', 'user_settings.json')
SESSIONS_PATH = os.path.join(PROJECTS_BASE, 'AgenticNovelStudio', 'Agent', 'sessions.json')
MATERIALS_INDEX = os.path.join(PROJECTS_BASE, 'AgenticNovelStudio', 'Materials', 'materials_index.json')

# Default user to assign migrated data to (first real user)
DEFAULT_USER_ID = None

def get_default_user(conn):
    """Get or create a default user for migrated data."""
    global DEFAULT_USER_ID
    if DEFAULT_USER_ID:
        return DEFAULT_USER_ID
    row = conn.execute("SELECT id FROM users ORDER BY created_at LIMIT 1").fetchone()
    if row:
        DEFAULT_USER_ID = row[0]
    else:
        DEFAULT_USER_ID = str(uuid.uuid4())
        conn.execute(
            "INSERT INTO users (id, username, email, password_hash, role, created_at) VALUES (?, 'migrated', 'migrated@local', '', 'author', ?)",
            (DEFAULT_USER_ID, datetime.now(timezone.utc).isoformat())
        )
    return DEFAULT_USER_ID


def now_iso():
    return datetime.now(timezone.utc).isoformat()


def safe_str(val):
    """Convert any value to a string suitable for SQLite TEXT column."""
    if val is None:
        return ''
    if isinstance(val, (dict, list)):
        return json.dumps(val, ensure_ascii=False)
    return str(val)


def migrate_projects(conn):
    """Migrate JSON projects to novel_projects table."""
    projects_file = os.path.join(PROJECTS_BASE, 'AgenticNovelStudio', 'NovelProjects', 'projects.json')
    if not os.path.exists(projects_file):
        print("  [SKIP] projects.json not found")
        return {}

    with open(projects_file) as f:
        data = json.load(f)

    user_id = get_default_user(conn)
    id_map = {}  # json_id -> sqlite_id (same id)

    for proj in data.get('projects', []):
        pid = proj.get('id', '')
        if not pid:
            continue

        existing = conn.execute("SELECT id FROM novel_projects WHERE id = ?", (pid,)).fetchone()
        if existing:
            print(f"  [SKIP] Project {pid[:12]}... already exists")
            id_map[pid] = pid
            continue

        title = proj.get('title', '未命名')
        genre = proj.get('genre', '')
        sub_genre = proj.get('subGenre', '')
        core_hook = proj.get('coreHook', '')
        status = proj.get('status', 'draft')
        storage_name = proj.get('storageProjectName', '')
        created = proj.get('createdAt', now_iso())
        updated = proj.get('updatedAt', now_iso())

        conn.execute(
            """INSERT INTO novel_projects (id, user_id, title, genre, sub_genre, core_hook, status, storage_project_name, created_at, updated_at)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (pid, user_id, title, genre, sub_genre, core_hook, status, storage_name, created, updated)
        )
        id_map[pid] = pid
        print(f"  [OK] Project: {title} ({pid[:12]}...)")

    return id_map


def migrate_story_bible(conn, project_id, storage_name, user_id):
    """Migrate a single story_bible.json to normalized SQLite tables."""
    sb_path = os.path.join(PROJECTS_BASE, storage_name, 'Services', 'Framework', 'AI', 'NovelAgent', 'story_bible.json')
    if not os.path.exists(sb_path):
        return

    with open(sb_path) as f:
        sb = json.load(f)

    # 1. Story Constitution
    c = sb.get('constitution')
    if c:
        existing = conn.execute("SELECT id FROM story_constitutions WHERE project_id = ?", (project_id,)).fetchone()
        if not existing:
            cid = str(uuid.uuid4()).replace('-', '')
            # Serialize complex fields to JSON strings
            genre_profile = c.get('genreProfile', '')
            if isinstance(genre_profile, (dict, list)):
                genre_profile = json.dumps(genre_profile, ensure_ascii=False)
            taboos = c.get('taboos') or c.get('forbiddenDirections', '')
            if isinstance(taboos, list):
                taboos = json.dumps(taboos, ensure_ascii=False)

            conn.execute(
                """INSERT INTO story_constitutions (id, user_id, project_id, genre, sub_genre, core_hook, reader_promise, genre_profile, target_audience, taboos, created_at, updated_at)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (cid, user_id, project_id,
                 c.get('genre', ''), c.get('subGenre', ''), c.get('coreHook', ''),
                 c.get('readerPromise', ''), genre_profile,
                 c.get('targetAudience', ''), taboos,
                 c.get('createdAt', now_iso()), c.get('updatedAt', now_iso()))
            )
            print(f"    [OK] Constitution: {c.get('genre')}/{c.get('subGenre')}")

    # 2. Volume Arcs
    for va in sb.get('volumeArcs', []):
        va_id = va.get('id', str(uuid.uuid4()).replace('-', ''))
        existing = conn.execute("SELECT id FROM volume_arcs WHERE id = ?", (va_id,)).fetchone()
        if existing:
            continue

        vol_num = va.get('volumeNumber') or 1
        chapter_beats = va.get('chapterBeats', [])
        key_events = json.dumps(chapter_beats, ensure_ascii=False) if chapter_beats else None

        conn.execute(
            """INSERT INTO volume_arcs (id, user_id, project_id, volume_number, volume_title, volume_theme,
               target_chapters, act1_setup, act2_confrontation, act3_climax, act4_resolution,
               key_events, major_conflict, conflict_escalation, status, created_at, updated_at)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (va_id, user_id, project_id, vol_num,
             va.get('title', ''), va.get('theme', ''),
             va.get('expectedChapterCount') or len(chapter_beats),
             va.get('entryState', ''), va.get('midpointReversal', ''),
             va.get('climax', ''), va.get('aftermathHook', ''),
             key_events, va.get('mainConflictUpgrade', ''),
             va.get('coreQuestion', ''), va.get('status', 'planned'),
             va.get('createdAt', now_iso()), va.get('updatedAt', now_iso()))
        )
        print(f"    [OK] VolumeArc: {va.get('title', '')} ({len(chapter_beats)} beats)")

    # 3. Foreshadow Ledger
    for fs in sb.get('foreshadowLedger', []):
        fs_id = fs.get('id', str(uuid.uuid4()).replace('-', ''))
        existing = conn.execute("SELECT id FROM foreshadow_ledger WHERE id = ?", (fs_id,)).fetchone()
        if existing:
            continue

        conn.execute(
            """INSERT INTO foreshadow_ledger (id, user_id, project_id, title, content, category,
               planted_in_chapter, planted_context, status, priority, planted_at, created_at, updated_at)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (fs_id, user_id, project_id,
             safe_str(fs.get('name', '')), safe_str(fs.get('description', '')), safe_str(fs.get('type', 'foreshadow')),
             safe_str(fs.get('plantedChapterId', '')), safe_str(fs.get('plantedContext', '')),
             safe_str(fs.get('status', 'planted')), fs.get('importance', 5),
             fs.get('createdAt', now_iso()), fs.get('createdAt', now_iso()), fs.get('updatedAt', now_iso()))
        )
        print(f"    [OK] Foreshadow: {fs.get('name', '')}")

    # 4. Characters (from characterLedger)
    for ch in sb.get('characterLedger', []):
        ch_id = ch.get('id', str(uuid.uuid4()).replace('-', ''))
        existing = conn.execute("SELECT id FROM characters WHERE id = ?", (ch_id,)).fetchone()
        if existing:
            continue

        conn.execute(
            """INSERT INTO characters (id, user_id, project_id, name, role, personality, background,
               core_goal, motivation, status, created_at, updated_at)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (ch_id, user_id, project_id,
             safe_str(ch.get('characterName', '')), safe_str(ch.get('role', '')),
             safe_str(ch.get('psychology', '')), safe_str(ch.get('summary', '')),
             safe_str(ch.get('currentGoal', '')), safe_str(ch.get('currentIntent', '')),
             safe_str(ch.get('status', 'active')),
             ch.get('createdAt', now_iso()), ch.get('updatedAt', now_iso()))
        )
        print(f"    [OK] Character: {ch.get('characterName', '')} ({ch.get('role', '')})")

    # 5. World Settings (from canonLedger)
    for cl in sb.get('canonLedger', []):
        cl_id = cl.get('id', str(uuid.uuid4()).replace('-', ''))
        existing = conn.execute("SELECT id FROM world_settings WHERE id = ?", (cl_id,)).fetchone()
        if existing:
            continue

        conn.execute(
            """INSERT INTO world_settings (id, user_id, project_id, category, title, content, created_at, updated_at)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?)""",
            (cl_id, user_id, project_id,
             safe_str(cl.get('type', 'canon')), safe_str(cl.get('title', '')), safe_str(cl.get('content', '')),
             cl.get('createdAt', now_iso()), cl.get('updatedAt', now_iso()))
        )
        print(f"    [OK] WorldSetting: {cl.get('title', '')}")

    # 6. Agent Runs
    for r in sb.get('agentRuns', []):
        rid = r.get('runId', '')
        if not rid:
            continue
        existing = conn.execute("SELECT id FROM agent_runs WHERE id = ?", (rid,)).fetchone()
        if existing:
            continue

        intent_map = {1: 'CreateStoryFoundation', 2: 'CreateVolumeArc', 3: 'CreateChapterDraft', 4: 'ReviewChapter'}
        # NovelAgentRunStatus enum: 0=Draft, 1=Planning, 2=Retrieving, 3=AwaitingConfirmation, 4=Executing, 5=Validating, 6=Repairing, 7=Completed, 8=Failed
        status_map = {0: 'draft', 1: 'planning', 2: 'retrieving', 3: 'awaiting_confirmation', 4: 'executing', 5: 'validating', 6: 'repairing', 7: 'completed', 8: 'failed'}

        # Serialize full run data to output_data
        output_data = json.dumps(r, ensure_ascii=False, default=str)

        conn.execute(
            """INSERT INTO agent_runs (id, user_id, project_id, run_type, target_chapter_id, status,
               input_params, output_data, started_at, created_at, updated_at)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (rid, user_id, project_id,
             intent_map.get(r.get('intent'), 'unknown'),
             r.get('targetChapterId', ''),
             status_map.get(r.get('status'), 'unknown'),
             json.dumps({'userGoal': r.get('userGoal', '')}, ensure_ascii=False),
             output_data,
             r.get('createdAt', now_iso()), r.get('createdAt', now_iso()), r.get('updatedAt', now_iso()))
        )
        print(f"    [OK] AgentRun: {rid[:12]}... ({intent_map.get(r.get('intent'), '?')})")


def migrate_materials(conn, project_id, storage_name, user_id):
    """Migrate materials from JSON to SQLite."""
    index_path = os.path.join(PROJECTS_BASE, storage_name, 'Materials', 'materials_index.json')
    if not os.path.exists(index_path):
        return

    with open(index_path) as f:
        data = json.load(f)

    for mat in data if isinstance(data, list) else data.get('materials', data.get('entries', [])):
        mid = mat.get('id', '')
        if not mid:
            continue
        existing = conn.execute("SELECT id FROM materials WHERE id = ?", (mid,)).fetchone()
        if existing:
            continue

        # Read content file if exists
        content = ''
        content_path = os.path.join(PROJECTS_BASE, storage_name, 'Materials', f'{mid}.txt')
        if os.path.exists(content_path):
            with open(content_path) as f:
                content = f.read()

        tags = mat.get('tags', [])
        if isinstance(tags, list):
            tags = ','.join(tags)

        conn.execute(
            """INSERT INTO materials (id, user_id, project_id, title, category, content_type, content, file_path, tags, created_at)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (mid, user_id, project_id,
             mat.get('title', mat.get('fileName', '')),
             mat.get('category', mat.get('sourceType', '')),
             mat.get('contentType', 'text'),
             content,
             mat.get('filePath', ''),
             tags,
             mat.get('createdAt', now_iso()))
        )
        print(f"    [OK] Material: {mat.get('title', mat.get('fileName', ''))}")


def migrate_knowledge_base(conn, project_id, storage_name, user_id):
    """Migrate creative_knowledge_base.json to knowledge_base table."""
    kb_path = os.path.join(PROJECTS_BASE, storage_name, 'Services', 'Framework', 'AI', 'NovelAgent', 'creative_knowledge_base.json')
    if not os.path.exists(kb_path):
        return

    with open(kb_path) as f:
        data = json.load(f)

    entries = data if isinstance(data, list) else data.get('entries', [])
    for entry in entries:
        eid = entry.get('id', str(uuid.uuid4()).replace('-', ''))
        existing = conn.execute("SELECT id FROM knowledge_base WHERE id = ?", (eid,)).fetchone()
        if existing:
            continue

        cat_map = {0: 'genre_rule', 1: 'reader_promise', 2: 'theme_depth', 3: 'relationship',
                   4: 'emotion_arc', 5: 'trope_pattern', 6: 'anti_trope', 7: 'project_pattern'}

        conn.execute(
            """INSERT INTO knowledge_base (id, project_id, entry_type, title, content, usage_count, created_at)
               VALUES (?, ?, ?, ?, ?, ?, ?)""",
            (eid, project_id,
             cat_map.get(entry.get('category'), 'general'),
             entry.get('title', ''), entry.get('content', ''),
             entry.get('weight', 0),
             now_iso())
        )

    print(f"    [OK] KnowledgeBase: {len(entries)} entries")


def migrate_agent_memories(conn, project_id, storage_name, user_id):
    """Migrate memory JSON files to agent_memories table."""
    agent_dir = os.path.join(PROJECTS_BASE, storage_name, 'Agent')
    if not os.path.isdir(agent_dir):
        return

    for mem_type, filename in [('project', 'project_memory.json'), ('execution', 'execution_memory.json'), ('author', 'author_memory.json')]:
        fpath = os.path.join(agent_dir, filename)
        if not os.path.exists(fpath):
            continue

        with open(fpath) as f:
            data = json.load(f)

        mem_id = str(uuid.uuid4()).replace('-', '')
        existing = conn.execute(
            "SELECT id FROM agent_memories WHERE user_id = ? AND project_id = ? AND memory_type = ?",
            (user_id, project_id, mem_type)
        ).fetchone()
        if existing:
            continue

        conn.execute(
            """INSERT INTO agent_memories (id, user_id, project_id, memory_type, content, updated_at)
               VALUES (?, ?, ?, ?, ?, ?)""",
            (mem_id, user_id, project_id, mem_type, json.dumps(data, ensure_ascii=False), now_iso())
        )
        print(f"    [OK] Memory: {mem_type}")


def migrate_sessions(conn, user_id):
    """Migrate sessions.json to agent_sessions table."""
    if not os.path.exists(SESSIONS_PATH):
        print("  [SKIP] sessions.json not found")
        return

    with open(SESSIONS_PATH) as f:
        sessions = json.load(f)

    for sess in sessions:
        sid = sess.get('sessionId', '')
        if not sid:
            continue

        existing = conn.execute("SELECT id FROM agent_sessions WHERE id = ?", (sid,)).fetchone()
        if existing:
            # Update session_data for existing sessions that have empty data
            existing_data = conn.execute("SELECT session_data FROM agent_sessions WHERE id = ?", (sid,)).fetchone()
            if existing_data and (not existing_data[0] or existing_data[0] == '{}'):
                session_data = json.dumps(sess, ensure_ascii=False, default=str)
                conn.execute("UPDATE agent_sessions SET session_data = ?, updated_at = ? WHERE id = ?",
                             (session_data, now_iso(), sid))
                print(f"  [UPDATE] Session {sid[:12]}... (filled session_data)")
            continue

        title = sess.get('title', '新会话')
        project_id = sess.get('activeProjectId', '')
        is_archived = 1 if sess.get('isArchived') else 0
        session_data = json.dumps(sess, ensure_ascii=False, default=str)
        created = sess.get('createdAt', now_iso())
        updated = sess.get('updatedAt', now_iso())

        conn.execute(
            """INSERT INTO agent_sessions (id, user_id, project_id, title, is_archived, session_data, created_at, updated_at)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?)""",
            (sid, user_id, project_id or None, title, is_archived, session_data, created, updated)
        )
        print(f"  [OK] Session: {title} ({sid[:12]}...)")


def migrate_user_settings(conn, user_id):
    """Update user_settings with missing fields from JSON file."""
    if not os.path.exists(SETTINGS_PATH):
        print("  [SKIP] user_settings.json not found")
        return

    with open(SETTINGS_PATH) as f:
        settings = json.load(f)

    existing = conn.execute("SELECT user_id FROM user_settings WHERE user_id = ?", (user_id,)).fetchone()
    if not existing:
        print(f"  [SKIP] No user_settings row for user {user_id[:12]}...")
        return

    # Update with values from JSON that may be missing in SQLite
    updates = {}
    if settings.get('llmProvider'):
        updates['llm_provider'] = settings['llmProvider']
    if settings.get('llmApiKey'):
        updates['llm_api_key_encrypted'] = settings['llmApiKey']
    if settings.get('llmBaseUrl'):
        updates['llm_base_url'] = settings['llmBaseUrl']
    if settings.get('llmModel'):
        updates['llm_model'] = settings['llmModel']
    if settings.get('llmTemperature') is not None:
        updates['llm_temperature'] = settings['llmTemperature']
    if settings.get('llmMaxTokens') is not None:
        updates['llm_max_tokens'] = settings['llmMaxTokens']
    if settings.get('embeddingProvider'):
        updates['embedding_provider'] = settings['embeddingProvider']
    if settings.get('embeddingModel'):
        updates['embedding_model'] = settings['embeddingModel']
    if settings.get('defaultGenre'):
        updates['default_genre'] = settings['defaultGenre']
    if settings.get('defaultChapterWordCount') is not None:
        updates['default_chapter_word_count'] = settings['defaultChapterWordCount']
    if settings.get('theme'):
        updates['theme'] = settings['theme']
    if settings.get('language'):
        updates['language'] = settings['language']

    if updates:
        set_clause = ', '.join(f'{k} = ?' for k in updates)
        values = list(updates.values()) + [user_id]
        conn.execute(f"UPDATE user_settings SET {set_clause} WHERE user_id = ?", values)
        print(f"  [OK] Updated user_settings with {len(updates)} fields from JSON")


def main():
    print("=" * 60)
    print("JSON → SQLite Migration")
    print("=" * 60)

    conn = sqlite3.connect(DB_PATH)
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA foreign_keys=ON")

    user_id = get_default_user(conn)
    print(f"\nDefault user: {user_id[:12]}...\n")

    # 1. Projects
    print("[1/7] Migrating projects...")
    id_map = migrate_projects(conn)

    # 2-6. Story Bible + Materials + Knowledge + Memories (per project)
    print("\n[2/7] Migrating story bible, materials, knowledge, memories...")
    projects_file = os.path.join(PROJECTS_BASE, 'AgenticNovelStudio', 'NovelProjects', 'projects.json')
    if os.path.exists(projects_file):
        with open(projects_file) as f:
            data = json.load(f)
        for proj in data.get('projects', []):
            pid = proj.get('id', '')
            sname = proj.get('storageProjectName', '')
            if not pid or not sname:
                continue
            print(f"\n  Project: {proj.get('title', '')} ({pid[:12]}...)")
            migrate_story_bible(conn, pid, sname, user_id)
            migrate_materials(conn, pid, sname, user_id)
            migrate_knowledge_base(conn, pid, sname, user_id)
            migrate_agent_memories(conn, pid, sname, user_id)

    # 7. Sessions
    print("\n[3/7] Migrating agent sessions...")
    migrate_sessions(conn, user_id)

    # 8. User settings
    print("\n[4/7] Migrating user settings...")
    migrate_user_settings(conn, user_id)

    # Summary
    print("\n" + "=" * 60)
    print("Migration Summary:")
    for table in ['novel_projects', 'story_constitutions', 'volume_arcs', 'foreshadow_ledger',
                   'characters', 'world_settings', 'agent_runs', 'materials', 'knowledge_base',
                   'agent_memories', 'agent_sessions']:
        count = conn.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]
        print(f"  {table}: {count} rows")

    conn.commit()
    conn.close()
    print("\n[DONE] Migration complete.")


if __name__ == '__main__':
    main()

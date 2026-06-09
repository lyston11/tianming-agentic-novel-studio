# Migration Testing Guide

This guide helps you test the data migration script before running it on production data.

## Pre-Migration Checklist

1. **Verify Source Data Exists**
   ```bash
   ls -la Web/NovelAgentWeb/App_Data/Projects/AgenticNovelStudio/NovelProjects/projects.json
   ls -la Web/NovelAgentWeb/App_Data/Projects/AgenticNovelStudio/Settings/user_settings.json
   ```

2. **Check Database Does Not Exist** (for first run)
   ```bash
   ls Web/NovelAgentWeb/App_Data/novel_agent.db
   # Should not exist for first migration
   ```

3. **Backup Existing Data** (recommended)
   ```bash
   cp -r Web/NovelAgentWeb/App_Data/Projects Web/NovelAgentWeb/App_Data/Projects.backup
   ```

## Test Migration on Sample Data

### Option 1: Test with Real Data (Recommended)

```bash
# 1. Build the migration tool
cd Scripts/Migration
dotnet build

# 2. Run migration with confirmation prompt
dotnet run

# 3. Verify results
dotnet run --project VerificationTool.cs
```

### Option 2: Test with Copy of Data

```bash
# 1. Create test directory
mkdir -p /tmp/novel-agent-test/App_Data/Projects
cp -r Web/NovelAgentWeb/App_Data/Projects/* /tmp/novel-agent-test/App_Data/Projects/

# 2. Run migration on test data
cd Scripts/Migration
dotnet run -- -a /tmp/novel-agent-test/App_Data -d /tmp/novel-agent-test/test.db -y

# 3. Check results
sqlite3 /tmp/novel-agent-test/test.db "SELECT COUNT(*) FROM users;"
sqlite3 /tmp/novel-agent-test/test.db "SELECT COUNT(*) FROM novel_projects;"
```

## Manual Verification Steps

### 1. Check Record Counts

```bash
# Using sqlite3
sqlite3 Web/NovelAgentWeb/App_Data/novel_agent.db <<EOF
.mode column
.headers on
SELECT 'users' as table_name, COUNT(*) as count FROM users
UNION ALL SELECT 'novel_projects', COUNT(*) FROM novel_projects
UNION ALL SELECT 'volumes', COUNT(*) FROM volumes
UNION ALL SELECT 'chapters', COUNT(*) FROM chapters
UNION ALL SELECT 'characters', COUNT(*) FROM characters
UNION ALL SELECT 'foreshadows', COUNT(*) FROM foreshadows
UNION ALL SELECT 'world_settings', COUNT(*) FROM world_settings
UNION ALL SELECT 'user_settings', COUNT(*) FROM user_settings
UNION ALL SELECT 'agent_memories', COUNT(*) FROM agent_memories
UNION ALL SELECT 'agent_sessions', COUNT(*) FROM agent_sessions;
EOF
```

### 2. Verify Foreign Keys

```bash
sqlite3 Web/NovelAgentWeb/App_Data/novel_agent.db <<EOF
-- Check for orphaned projects (projects without users)
SELECT COUNT(*) as orphaned_projects 
FROM novel_projects p 
WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.id = p.user_id);

-- Check for orphaned chapters (chapters without projects)
SELECT COUNT(*) as orphaned_chapters 
FROM chapters c 
WHERE NOT EXISTS (SELECT 1 FROM novel_projects p WHERE p.id = c.project_id);

-- Check for orphaned characters (characters without projects)
SELECT COUNT(*) as orphaned_characters 
FROM characters ch 
WHERE NOT EXISTS (SELECT 1 FROM novel_projects p WHERE p.id = ch.project_id);
EOF
```

### 3. Check Admin User

```bash
sqlite3 Web/NovelAgentWeb/App_Data/novel_agent.db <<EOF
.mode column
.headers on
SELECT id, username, email, role, is_active, created_at 
FROM users 
WHERE username = 'admin';
EOF
```

### 4. List All Projects

```bash
sqlite3 Web/NovelAgentWeb/App_Data/novel_agent.db <<EOF
.mode column
.headers on
SELECT id, title, genre, status, storage_project_name 
FROM novel_projects;
EOF
```

### 5. Check Project Details

```bash
PROJECT_ID="<your-project-id>"

sqlite3 Web/NovelAgentWeb/App_Data/novel_agent.db <<EOF
.mode column
.headers on

SELECT 'Project' as entity, title as name FROM novel_projects WHERE id = '$PROJECT_ID'
UNION ALL
SELECT 'Chapters', CAST(COUNT(*) AS TEXT) FROM chapters WHERE project_id = '$PROJECT_ID'
UNION ALL
SELECT 'Characters', CAST(COUNT(*) AS TEXT) FROM characters WHERE project_id = '$PROJECT_ID'
UNION ALL
SELECT 'Foreshadows', CAST(COUNT(*) AS TEXT) FROM foreshadows WHERE project_id = '$PROJECT_ID'
UNION ALL
SELECT 'WorldSettings', CAST(COUNT(*) AS TEXT) FROM world_settings WHERE project_id = '$PROJECT_ID';
EOF
```

## Compare JSON vs Database

### Count Projects in JSON

```bash
cat Web/NovelAgentWeb/App_Data/Projects/AgenticNovelStudio/NovelProjects/projects.json | jq '.projects | length'
```

### Count Projects in Database

```bash
sqlite3 Web/NovelAgentWeb/App_Data/novel_agent.db "SELECT COUNT(*) FROM novel_projects;"
```

### List Projects from Both Sources

```bash
echo "=== JSON Projects ==="
cat Web/NovelAgentWeb/App_Data/Projects/AgenticNovelStudio/NovelProjects/projects.json | jq -r '.projects[] | "\(.title) (\(.id))"'

echo ""
echo "=== Database Projects ==="
sqlite3 Web/NovelAgentWeb/App_Data/novel_agent.db "SELECT title || ' (' || storage_project_name || ')' FROM novel_projects;"
```

## Test Cases

### Test 1: Empty Database Migration
- **Setup:** Fresh database, no existing data
- **Expected:** Migration succeeds, creates admin user and all records
- **Command:** `dotnet run`

### Test 2: Force Re-Migration
- **Setup:** Database already has data
- **Expected:** Migration fails without --force, succeeds with --force
- **Commands:**
  ```bash
  dotnet run                 # Should fail
  dotnet run -- --force -y   # Should succeed
  ```

### Test 3: No Backup Mode
- **Setup:** Run migration without backup
- **Expected:** Migration runs faster, no backup directory created
- **Command:** `dotnet run -- --no-backup -y`

### Test 4: Missing Source Files
- **Setup:** Rename projects.json temporarily
- **Expected:** Migration creates admin but no projects
- **Commands:**
  ```bash
  mv Web/NovelAgentWeb/App_Data/Projects/AgenticNovelStudio/NovelProjects/projects.json projects.json.bak
  dotnet run -- -y
  mv projects.json.bak Web/NovelAgentWeb/App_Data/Projects/AgenticNovelStudio/NovelProjects/projects.json
  ```

## Rollback Procedure

If migration fails or produces incorrect results:

```bash
# 1. Delete the database
rm Web/NovelAgentWeb/App_Data/novel_agent.db

# 2. Restore from backup (if backup was created)
BACKUP_DIR=$(ls -td Web/NovelAgentWeb/App_Data/Backup/* | head -1)
echo "Restoring from: $BACKUP_DIR"
# Original files are still intact, backup is just a copy

# 3. Fix any issues and re-run
cd Scripts/Migration
dotnet run
```

## Performance Benchmarks

Expected migration times (approximate):

- 1-3 projects: < 5 seconds
- 10 projects: < 15 seconds  
- 50 projects: < 1 minute
- 100+ projects: 2-5 minutes

If migration takes significantly longer, check for:
- Large chapter files
- Network storage issues
- Insufficient memory
- Database locking issues

## Troubleshooting

### Issue: "Database already contains data"
**Solution:** Use `--force` flag or delete the database first

### Issue: "Projects file not found"
**Solution:** Verify App_Data path is correct and projects.json exists

### Issue: BCrypt.Net error
**Solution:** Run `dotnet restore` to ensure packages are installed

### Issue: Foreign key constraint violations
**Solution:** Check that user_id exists in users table before creating projects

### Issue: Null reference exceptions
**Solution:** Check that JSON files are well-formed and contain expected fields

## Success Criteria

Migration is successful when:

- ✓ No errors during migration
- ✓ Admin user exists with username "admin"
- ✓ All projects from JSON are in database
- ✓ Project counts match between JSON and database
- ✓ No orphaned foreign key references
- ✓ Chapter content_path points to existing files
- ✓ User settings migrated correctly
- ✓ Agent memories imported
- ✓ Backup created (unless --no-backup used)

Run the verification tool to check all criteria:
```bash
cd Scripts/Migration
dotnet run --project VerificationTool.cs
```
